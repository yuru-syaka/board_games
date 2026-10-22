import * as cdk from 'aws-cdk-lib/core';
import * as acm from 'aws-cdk-lib/aws-certificatemanager';
import * as cognito from 'aws-cdk-lib/aws-cognito';
import * as route53 from 'aws-cdk-lib/aws-route53';
import * as route53targets from 'aws-cdk-lib/aws-route53-targets';
import * as ssm from 'aws-cdk-lib/aws-ssm';
import { Construct } from 'constructs';

export interface AuthStackProps extends cdk.StackProps {
  domainName: string;
  /** CertStack（us-east-1）で発行した auth.<domain> の証明書 */
  authCertificate: acm.ICertificate;
  /**
   * Google ログイン（ステップ 5-6）。Google Cloud の OAuth クライアントの ID とシークレット。
   * GitHub Actions の Secrets（GOOGLE_CLIENT_ID・GOOGLE_CLIENT_SECRET）から渡す。公開リポジトリなのでコードには書かない。
   * 無いときは、このスタックをデプロイできないようにする（Google ログインを消してしまわないように）。
   */
  google?: { clientId: string; clientSecret: string };
}

/**
 * ログイン（ステップ 5）。Amazon Cognito のユーザープール。
 *   - メールアドレスとパスワード、または Google でログイン（ステップ 5-6）。ログインしなくてもゲームは遊べる
 *   - ログイン画面は AWS の managed login（https://auth.<domain>、lang=ja で日本語）。
 *     日本語の画面は Essentials プラン以上（Lite は英語の画面だけ）。月 10,000 人までは無料
 *   - サイト（js/auth.js）が認可コード ＋ PKCE でログインし、ID トークンをゲームと API に渡す
 * GameStack（手元からデプロイ）にはスタックの出力ではなく SSM パラメータで値を渡す
 * （GitHub Actions は GameStack を読み込まないため、出力で渡すと CI のデプロイで消そうとして失敗する）。
 */
export class AuthStack extends cdk.Stack {
  static readonly userPoolIdParam = '/boardpia/auth/user-pool-id';
  static readonly clientIdParam = '/boardpia/auth/client-id';

  readonly userPool: cognito.UserPool;
  readonly client: cognito.UserPoolClient;
  /** ログイン画面のドメイン（auth.<domain>） */
  readonly authDomain: string;

  constructor(scope: Construct, id: string, props: AuthStackProps) {
    super(scope, id, props);
    const site = `https://${props.domainName}`;
    this.authDomain = `auth.${props.domainName}`;

    const userPool = new cognito.UserPool(this, 'UserPool', {
      userPoolName: 'boardpia-users',
      featurePlan: cognito.FeaturePlan.ESSENTIALS,
      selfSignUpEnabled: true,
      signInAliases: { email: true },
      signInCaseSensitive: false,
      autoVerify: { email: true },
      standardAttributes: { email: { required: true, mutable: true } },
      keepOriginal: { email: true }, // 新しいアドレスを確認するまでは、前のアドレスのまま
      passwordPolicy: {
        minLength: 8,
        requireLowercase: false,
        requireUppercase: false,
        requireDigits: false,
        requireSymbols: false,
      },
      accountRecovery: cognito.AccountRecovery.EMAIL_ONLY,
      // 確認コードのメール（送信元は Cognito の標準。1 日 50 通まで。超えそうになったら SES に切り替える）
      userVerification: {
        emailStyle: cognito.VerificationEmailStyle.CODE,
        emailSubject: 'BOARD PIA 確認コード',
        emailBody: 'BOARD PIA をご利用いただきありがとうございます。\n確認コード: {####}\n\n' +
          'このメールに心当たりがない場合は、何もせずに削除してください。',
      },
      email: cognito.UserPoolEmail.withCognito(),
      // ユーザーの一覧は作り直せないので、スタックを消しても残す。誤って消せないようにもする
      removalPolicy: cdk.RemovalPolicy.RETAIN,
      deletionProtection: true,
    });

    // Google ログイン（ステップ 5-6）。Google のアカウントは、メールで作ったアカウントとは別のアカウントになる
    // （同じメールアドレスでも、つなげない）。Google Cloud に登録するリダイレクト先は https://auth.<domain>/oauth2/idpresponse
    let google: cognito.UserPoolIdentityProviderGoogle | undefined;
    if (props.google) {
      google = new cognito.UserPoolIdentityProviderGoogle(this, 'Google', {
        userPool,
        clientId: props.google.clientId,
        clientSecretValue: cdk.SecretValue.unsafePlainText(props.google.clientSecret),
        scopes: ['openid', 'email'], // メールアドレスだけを受け取る（名前やプロフィール写真は受け取らない）
        attributeMapping: {
          email: cognito.ProviderAttribute.GOOGLE_EMAIL,
          emailVerified: cognito.ProviderAttribute.other('email_verified'),
        },
      });
    } else {
      // 手元で `cdk deploy PlayStack`（依存で AuthStack も含まれる）などをしたとき、Google ログインを消さないように止める。
      // 手元からは `cdk deploy PlayStack --exclusively` で、AuthStack に触れずにデプロイする
      cdk.Annotations.of(this).addError(
        'Google ログインの設定（環境変数 GOOGLE_CLIENT_ID・GOOGLE_CLIENT_SECRET）がありません。' +
        'AuthStack は GitHub Actions からデプロイします。手元から PlayStack などを出すときは --exclusively を付けてください。');
    }

    // サイト（ブラウザ）用のクライアント。ブラウザには秘密を置けないので、シークレットなし ＋ PKCE
    const callbackPath = '/auth/callback.html';
    const client = userPool.addClient('WebClient', {
      userPoolClientName: 'boardpia-web',
      generateSecret: false,
      authFlows: { userSrp: true },
      oAuth: {
        flows: { authorizationCodeGrant: true },
        scopes: [cognito.OAuthScope.OPENID, cognito.OAuthScope.EMAIL],
        // localhost:8080 は手元の確認用（node scripts/serve-web.js など）
        callbackUrls: [site + callbackPath, 'http://localhost:8080' + callbackPath],
        logoutUrls: [site + '/', 'http://localhost:8080/'],
      },
      supportedIdentityProviders: [
        cognito.UserPoolClientIdentityProvider.COGNITO,
        ...(google ? [cognito.UserPoolClientIdentityProvider.GOOGLE] : []),
      ],
      idTokenValidity: cdk.Duration.hours(1),
      accessTokenValidity: cdk.Duration.hours(1),
      refreshTokenValidity: cdk.Duration.days(30), // 30 日ごとにログインし直す
      preventUserExistenceErrors: true, // 「そのメールアドレスは登録されていない」を外から見分けられないように
      enableTokenRevocation: true,
    });
    if (google) client.node.addDependency(google); // Google を作ってから、クライアントで使えるようにする

    // ログイン画面のドメイン（https://auth.boardgamepia.com）。親のドメインに A レコードがある必要がある（ある）
    const domain = userPool.addDomain('Domain', {
      customDomain: { domainName: this.authDomain, certificate: props.authCertificate },
      managedLoginVersion: cognito.ManagedLoginVersion.NEWER_MANAGED_LOGIN,
    });
    // managed login の見た目。まずは AWS の標準のまま（色を変えるならここ）
    new cognito.CfnManagedLoginBranding(this, 'LoginBranding', {
      userPoolId: userPool.userPoolId,
      clientId: client.userPoolClientId,
      useCognitoProvidedValues: true,
    });

    // ログイン画面（新規登録）に、利用規約とプライバシーポリシーへのリンクを出す（ステップ 5-5）。
    // 両方そろうと表示される。ページの URL を変えたら、ここも直す
    for (const [id, termsName, page] of [['TermsOfUse', 'terms-of-use', 'terms.html'], ['PrivacyPolicy', 'privacy-policy', 'privacy.html']]) {
      new cognito.CfnTerms(this, id, {
        userPoolId: userPool.userPoolId,
        clientId: client.userPoolClientId,
        termsName,
        termsSource: 'LINK',
        enforcement: 'NONE',
        links: { 'cognito:default': `${site}/${page}`, 'cognito:japanese': `${site}/${page}` },
      });
    }

    const zone = route53.HostedZone.fromLookup(this, 'Zone', { domainName: props.domainName });
    new route53.ARecord(this, 'AuthRecord', {
      zone,
      recordName: this.authDomain,
      target: route53.RecordTarget.fromAlias(new route53targets.UserPoolDomainTarget(domain)),
    });

    new ssm.StringParameter(this, 'UserPoolIdParam', {
      parameterName: AuthStack.userPoolIdParam,
      stringValue: userPool.userPoolId,
    });
    new ssm.StringParameter(this, 'ClientIdParam', {
      parameterName: AuthStack.clientIdParam,
      stringValue: client.userPoolClientId,
    });

    this.userPool = userPool;
    this.client = client;
    new cdk.CfnOutput(this, 'UserPoolId', { value: userPool.userPoolId });
    new cdk.CfnOutput(this, 'ClientId', { value: client.userPoolClientId });
    new cdk.CfnOutput(this, 'LoginUrl', { value: `https://${this.authDomain}` });
  }
}
