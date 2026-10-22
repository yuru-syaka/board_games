import * as path from 'path';
import * as cdk from 'aws-cdk-lib/core';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as s3deploy from 'aws-cdk-lib/aws-s3-deployment';
import * as cloudfront from 'aws-cdk-lib/aws-cloudfront';
import * as origins from 'aws-cdk-lib/aws-cloudfront-origins';
import * as acm from 'aws-cdk-lib/aws-certificatemanager';
import * as route53 from 'aws-cdk-lib/aws-route53';
import * as route53targets from 'aws-cdk-lib/aws-route53-targets';
import * as ssm from 'aws-cdk-lib/aws-ssm';
import { Construct } from 'constructs';
import { GameStack } from './game-stack';

export interface WebStackProps extends cdk.StackProps {
  /** 独自ドメイン（例: boardgamepia.com）。www 付きはこのドメインへ転送する */
  domainName: string;
  /** CertStack（us-east-1）で発行した証明書 */
  certificate: acm.ICertificate;
  /** ログイン（AuthStack）。サイトの js/auth-config.js に書き出す */
  auth: { domain: string; clientId: string };
}

/**
 * サイト配信: 非公開の S3 バケット + CloudFront（OAC で CloudFront からだけ読める）。
 * web/ の中身をデプロイ時にアップロードし、CloudFront のキャッシュも消す。
 * 独自ドメイン（apex と www）を CloudFront に向け、www は apex へ 301 転送する。
 * *.cloudfront.net の URL も引き続き使える。
 */
export class WebStack extends cdk.Stack {
  /** PlayStack に値を渡すための SSM パラメータ名 */
  static readonly bucketNameParam = '/boardpia/web/site-bucket-name';
  static readonly distributionIdParam = '/boardpia/web/distribution-id';

  readonly distribution: cloudfront.Distribution;
  readonly bucket: s3.Bucket;

  constructor(scope: Construct, id: string, props: WebStackProps) {
    super(scope, id, props);
    const domain = props.domainName;

    const bucket = new s3.Bucket(this, 'SiteBucket', {
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      encryption: s3.BucketEncryption.S3_MANAGED,
      enforceSSL: true,
      // 開発中は cdk destroy で中身ごと消せるようにしておく
      removalPolicy: cdk.RemovalPolicy.DESTROY,
      autoDeleteObjects: true,
    });

    // www 付きのアクセスは apex へ転送し、
    // "/blog/" や "/blog" を "/blog/index.html" に読み替える（S3 は自動でディレクトリの index を返さないため）
    const indexRewrite = new cloudfront.Function(this, 'IndexRewrite', {
      runtime: cloudfront.FunctionRuntime.JS_2_0,
      code: cloudfront.FunctionCode.fromInline(`
function handler(event) {
  var request = event.request;
  var uri = request.uri;
  var host = request.headers.host ? request.headers.host.value : '';
  if (host === 'www.${domain}') {
    var qs = Object.keys(request.querystring).map(function (k) {
      var v = request.querystring[k].value;
      return v ? k + '=' + v : k;
    }).join('&');
    return {
      statusCode: 301,
      statusDescription: 'Moved Permanently',
      headers: { location: { value: 'https://${domain}' + uri + (qs ? '?' + qs : '') } },
    };
  }
  if (uri.endsWith('/')) {
    request.uri = uri + 'index.html';
  } else if (!uri.split('/').pop().includes('.')) {
    request.uri = uri + '/index.html';
  }
  return request;
}`),
    });

    // ブラウザ側の防御機能を有効にするレスポンスヘッダー
    // Google AdSense が使うドメイン（広告の読み込み・表示・計測・同意メッセージ）
    const ads = [
      'https://pagead2.googlesyndication.com',
      'https://*.googlesyndication.com',
      'https://*.doubleclick.net',
      'https://*.googleadservices.com',
      'https://adservice.google.com',
      'https://*.google.com',
      'https://*.gstatic.com',
      'https://*.adtrafficquality.google',
      'https://fundingchoicesmessages.google.com',
    ].join(' ');
    const csp = [
      "default-src 'self'",
      // Unity WebGL の起動に wasm の実行とインラインの起動スクリプトが必要
      `script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval' blob: ${ads}`,
      "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
      "font-src 'self' https://fonts.gstatic.com",
      "img-src 'self' data: blob: https:", // 広告の画像は配信元が多いため https を許可
      "media-src 'self' blob:",
      // ws.<domain> は自前のオンライン対戦サーバー（GameStack）。Photon は 2026-10-21 に使うのをやめた
      // auth.<domain> はログイン（Cognito）。トークンの受け取りと更新に使う
      `connect-src 'self' blob: data: wss://ws.${domain} https://${props.auth.domain} ${ads}`,
      `frame-src ${ads}`,
      "worker-src 'self' blob:",
      "frame-ancestors 'self'",
      "object-src 'none'",
      "base-uri 'self'",
    ].join('; ');

    const securityHeaders = new cloudfront.ResponseHeadersPolicy(this, 'SecurityHeaders', {
      comment: 'board pia security headers',
      securityHeadersBehavior: {
        strictTransportSecurity: { accessControlMaxAge: cdk.Duration.days(365), includeSubdomains: false, override: true },
        contentTypeOptions: { override: true },
        frameOptions: { frameOption: cloudfront.HeadersFrameOption.SAMEORIGIN, override: true },
        referrerPolicy: { referrerPolicy: cloudfront.HeadersReferrerPolicy.STRICT_ORIGIN_WHEN_CROSS_ORIGIN, override: true },
      },
      customHeadersBehavior: {
        customHeaders: [
          // まずは「報告のみ」。ブラウザのコンソールに違反が出ないことを確かめてから強制に切り替える
          { header: 'Content-Security-Policy-Report-Only', value: csp, override: true },
          { header: 'Permissions-Policy', value: 'camera=(), microphone=(), geolocation=(), payment=()', override: true },
        ],
      },
    });

    // ランキング・プロフィール・マイページの API（ステップ 5-4。GameStack の HTTP API）。
    // 行き先のドメインは GameStack が SSM に書いたもの（GameStack を先にデプロイしておく）
    const apiDomain = ssm.StringParameter.valueForStringParameter(this, GameStack.apiDomainParam);
    // 覚えておく時間は API の Cache-Control に従う（ランキング・プロフィールは 60 秒、自分の情報は覚えない）。
    // ログインのトークン（Authorization）はキャッシュのキーに入れて API に渡す（ほかの人の情報が返らないように）
    const apiCachePolicy = new cloudfront.CachePolicy(this, 'ApiCachePolicy', {
      comment: 'board pia api (honors Cache-Control, max 60 s)',
      minTtl: cdk.Duration.seconds(0),
      defaultTtl: cdk.Duration.seconds(0),
      maxTtl: cdk.Duration.seconds(60),
      headerBehavior: cloudfront.CacheHeaderBehavior.allowList('Authorization'),
      queryStringBehavior: cloudfront.CacheQueryStringBehavior.none(),
      cookieBehavior: cloudfront.CacheCookieBehavior.none(),
      enableAcceptEncodingGzip: true,
      enableAcceptEncodingBrotli: true,
    });

    const distribution = new cloudfront.Distribution(this, 'SiteDistribution', {
      comment: 'board pia site',
      defaultRootObject: 'index.html',
      priceClass: cloudfront.PriceClass.PRICE_CLASS_200, // 日本を含み、高い地域を除く
      domainNames: [domain, `www.${domain}`],
      certificate: props.certificate,
      minimumProtocolVersion: cloudfront.SecurityPolicyProtocol.TLS_V1_2_2021, // 古い TLS 1.0 / 1.1 を使わせない
      defaultBehavior: {
        origin: origins.S3BucketOrigin.withOriginAccessControl(bucket),
        viewerProtocolPolicy: cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
        cachePolicy: cloudfront.CachePolicy.CACHING_OPTIMIZED,
        responseHeadersPolicy: securityHeaders,
        compress: true,
        functionAssociations: [
          { function: indexRewrite, eventType: cloudfront.FunctionEventType.VIEWER_REQUEST },
        ],
      },
      additionalBehaviors: {
        '/api/*': {
          origin: new origins.HttpOrigin(apiDomain, { protocolPolicy: cloudfront.OriginProtocolPolicy.HTTPS_ONLY }),
          viewerProtocolPolicy: cloudfront.ViewerProtocolPolicy.HTTPS_ONLY,
          allowedMethods: cloudfront.AllowedMethods.ALLOW_ALL, // 名前の変更（PUT）・アカウントの削除（DELETE）
          cachePolicy: apiCachePolicy,
          // Host 以外のヘッダー（Content-Type など）を API に渡す。Host は API Gateway 自身のものにする
          originRequestPolicy: cloudfront.OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
          responseHeadersPolicy: securityHeaders,
          compress: true,
        },
      },
      // 存在しない URL では、S3 のエラー（OAC ではファイルがないと 403）の代わりに 404.html を返す。
      // ステータスは 404 にそろえる（検索エンジンに「ないページ」と伝えるため）。
      // エラーのキャッシュは短めにして、ページを追加したときにすぐ見えるようにする。
      errorResponses: [403, 404].map((httpStatus) => ({
        httpStatus,
        responseHttpStatus: 404,
        responsePagePath: '/404.html',
        ttl: cdk.Duration.minutes(1),
      })),
    });

    // サイト（HTML・CSS・画像など）。play/（ゲームのビルド）は PlayStack に任せる。
    // exclude したファイルは prune（S3 側の不要ファイル削除）の対象にもならないので、ゲームのビルドは消えない。
    // GitHub Actions からデプロイしても（CI には play/ がない）、ここは同じ内容になる。
    const webDir = path.join(__dirname, '..', '..', 'web');
    // ログインの設定（js/auth.js が読む）。クライアント ID は AuthStack を作ったときに決まるので、ここで書き出す。
    // 秘密の値ではない（ブラウザに見える値）。手元で確かめるときは、同じ内容の web/js/auth-config.js（git 管理外）を置く
    const authConfig = s3deploy.Source.data('js/auth-config.js',
      `window.BOARDPIA_AUTH = ${JSON.stringify({ domain: props.auth.domain, clientId: props.auth.clientId })};\n`);
    new s3deploy.BucketDeployment(this, 'DeploySite', {
      // play フォルダ自体も除外する（あるかないかでアップロード内容のハッシュが変わらないように）。
      // 手元の auth-config.js も除外する（上で書き出すものと重ならないように）
      sources: [s3deploy.Source.asset(webDir, { exclude: ['play', 'play/**', 'js/auth-config.js'] }), authConfig],
      destinationBucket: bucket,
      exclude: ['play/*'],
      distribution,
      distributionPaths: ['/*'],
    });

    // Route 53: apex と www を CloudFront に向ける（IPv4 = A、IPv6 = AAAA）。CloudFront 向けのエイリアスは問い合わせ無料
    const zone = route53.HostedZone.fromLookup(this, 'Zone', { domainName: domain });
    const target = route53.RecordTarget.fromAlias(new route53targets.CloudFrontTarget(distribution));
    for (const [id, recordName] of [['Apex', domain], ['Www', `www.${domain}`]]) {
      new route53.ARecord(this, `${id}A`, { zone, recordName, target });
      new route53.AaaaRecord(this, `${id}Aaaa`, { zone, recordName, target });
    }

    // ドメイン直下の TXT レコード（所有権の確認などに使う）。
    // 同じ名前の TXT レコードは 1 つにまとめる必要があるので、値を増やすときはこの配列に追加する。
    new route53.TxtRecord(this, 'ApexTxt', {
      zone,
      recordName: domain,
      values: [
        // Google Search Console のドメイン所有権の確認（確認後も消さない。消すと所有権が外れる）
        'google-site-verification=W4vvExEzk3-1BBaCKojzyUnHnKiIgpO9xxE9Yv_EPnc',
      ],
      ttl: cdk.Duration.minutes(5),
    });

    this.distribution = distribution;
    this.bucket = bucket;

    // PlayStack（ゲームのビルド）に渡す値。スタックの出力ではなく SSM パラメータで渡すことで、
    // ゲームのビルドがない環境（GitHub Actions）から WebStack をデプロイしても、受け渡しが消えないようにする。
    new ssm.StringParameter(this, 'SiteBucketNameParam', {
      parameterName: WebStack.bucketNameParam,
      stringValue: bucket.bucketName,
    });
    new ssm.StringParameter(this, 'DistributionIdParam', {
      parameterName: WebStack.distributionIdParam,
      stringValue: distribution.distributionId,
    });
    new cdk.CfnOutput(this, 'SiteUrl', { value: `https://${domain}/` });
    new cdk.CfnOutput(this, 'CloudFrontUrl', { value: `https://${distribution.distributionDomainName}/` });
    new cdk.CfnOutput(this, 'BucketName', { value: bucket.bucketName });
  }
}
