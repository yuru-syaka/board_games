import { execFileSync } from 'child_process';
import * as crypto from 'crypto';
import * as fs from 'fs';
import * as path from 'path';
import * as cdk from 'aws-cdk-lib/core';
import * as acm from 'aws-cdk-lib/aws-certificatemanager';
import * as apigwv2 from 'aws-cdk-lib/aws-apigatewayv2';
import { HttpLambdaIntegration, WebSocketLambdaIntegration } from 'aws-cdk-lib/aws-apigatewayv2-integrations';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as logs from 'aws-cdk-lib/aws-logs';
import * as route53 from 'aws-cdk-lib/aws-route53';
import * as route53targets from 'aws-cdk-lib/aws-route53-targets';
import * as ssm from 'aws-cdk-lib/aws-ssm';
import { Construct } from 'constructs';
import { AuthStack } from './auth-stack';

export interface GameStackProps extends cdk.StackProps {
  domainName: string;
}

/**
 * オンライン対戦サーバー（wss://ws.<domain>）。費用を抑えるため、常に動いているサーバーは使わない。
 *   API Gateway（WebSocket）… ブラウザとつながり続ける。メッセージが届いたときだけ Lambda を呼ぶ
 *   Lambda（C#、.NET 10、arm64）… server/BoardGames.Server。ルールは shared/ の Unity と同じコード
 *   DynamoDB（オンデマンド）… 接続・待ち行列・ルームコード・対局（種 ＋ 手の一覧）。ttl で自動的に消える
 *                              対局結果は別のテーブル（期限なし）。プレイヤーの戦績・レートも別のテーブル（期限なし）
 *   ログイン（ステップ 5-2）: ブラウザが送る Cognito の ID トークンを Lambda が確かめる（AuthStack の値を SSM から読む）
 * 費用の安全装置: API Gateway のスロットリング（1 秒あたりの上限）、ログの保存は 1 週間。
 * ビルド: cdk deploy GameStack のときに、手元の dotnet publish で Lambda のコードを作る（Docker は使わない）。
 * GitHub Actions からはデプロイしない（PlayStack と同じく手元の PC から）。
 */
export class GameStack extends cdk.Stack {
  /** ランキングなどの API（HTTP API）のドメイン。WebStack の CloudFront が /api/* の行き先に使う */
  static readonly apiDomainParam = '/boardpia/api/domain';

  constructor(scope: Construct, id: string, props: GameStackProps) {
    super(scope, id, props);

    const repoRoot = path.join(__dirname, '..', '..');
    const wsDomain = `ws.${props.domainName}`;

    // ---------- DynamoDB ----------
    const table = new dynamodb.TableV2(this, 'GameTable', {
      partitionKey: { name: 'pk', type: dynamodb.AttributeType.STRING },
      billing: dynamodb.Billing.onDemand(),
      timeToLiveAttribute: 'ttl',
      // いずれ対局結果（戦績）も入るので、スタックを消してもテーブルは残す
      removalPolicy: cdk.RemovalPolicy.RETAIN,
    });

    // 対局結果（1 局 1 行。キー id = 対局ID#何局目）。期限なしで残す。Lambda は書き込むだけ
    const resultsTable = new dynamodb.TableV2(this, 'ResultsTable', {
      partitionKey: { name: 'id', type: dynamodb.AttributeType.STRING },
      billing: dynamodb.Billing.onDemand(),
      removalPolicy: cdk.RemovalPolicy.RETAIN,
      // 消えると戻せない記録（ステップ 6-1）: 過去 35 日の好きな時点に戻せるようにし（PITR）、誤って消せないようにする
      pointInTimeRecoverySpecification: { pointInTimeRecoveryEnabled: true },
      deletionProtection: true,
    });

    // プレイヤー（ステップ 5-2）。キー id = ユーザー ID、key = 行の種類（profile = 表示名、g#ゲーム = そのゲームの戦績）。
    // 索引 ranking: ゲームごとにレートの高い順に読む（5-4 のランキングの API が使う）。期限なしで残す
    const playersTable = new dynamodb.TableV2(this, 'PlayersTable', {
      partitionKey: { name: 'id', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'key', type: dynamodb.AttributeType.STRING },
      billing: dynamodb.Billing.onDemand(),
      removalPolicy: cdk.RemovalPolicy.RETAIN,
      // 消えると戻せない記録（ステップ 6-1）: 過去 35 日の好きな時点に戻せるようにし（PITR）、誤って消せないようにする
      pointInTimeRecoverySpecification: { pointInTimeRecoveryEnabled: true },
      deletionProtection: true,
      globalSecondaryIndexes: [{
        indexName: 'ranking',
        partitionKey: { name: 'rankGame', type: dynamodb.AttributeType.STRING },
        sortKey: { name: 'rating', type: dynamodb.AttributeType.NUMBER },
        // ランキングの表に要る項目だけを索引に写す（読む量を減らす）
        projectionType: dynamodb.ProjectionType.INCLUDE,
        nonKeyAttributes: ['games', 'wins', 'losses', 'draws', 'rated'],
      }],
    });

    // ---------- Lambda ----------
    const logGroup = new logs.LogGroup(this, 'GameServerLogs', {
      retention: logs.RetentionDays.ONE_WEEK,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    // 対戦サーバーと API は同じ C# のコード（入口だけが違う）
    const code = lambda.Code.fromAsset(repoRoot, {
      // server/ と shared/ の中身が変わったときだけ作り直す
      assetHashType: cdk.AssetHashType.CUSTOM,
      assetHash: hashSources([path.join(repoRoot, 'server'), path.join(repoRoot, 'shared')]),
      bundling: {
        image: cdk.DockerImage.fromRegistry('unused'), // Docker は使わない（local で作る）
        local: {
          tryBundle(outputDir: string) {
            execFileSync('dotnet', [
              'publish', path.join(repoRoot, 'server', 'BoardGames.Server', 'BoardGames.Server.csproj'),
              '-c', 'Release', '-r', 'linux-arm64', '--self-contained', 'false', '-o', outputDir,
            ], { stdio: 'inherit' });
            return true;
          },
        },
      },
    });
    // ログインの確認に使う（AuthStack が SSM に書いた値。デプロイのときに読む）
    const userPoolId = ssm.StringParameter.valueForStringParameter(this, AuthStack.userPoolIdParam);
    const userPoolClientId = ssm.StringParameter.valueForStringParameter(this, AuthStack.clientIdParam);

    const fn = new lambda.Function(this, 'GameServer', {
      runtime: lambda.Runtime.DOTNET_10,
      architecture: lambda.Architecture.ARM_64, // x86 より安い
      memorySize: 1024, // 起動直後の準備を速くする（CPU はメモリに比例する。ステップ 6-2）
      timeout: cdk.Duration.seconds(10),
      handler: 'BoardGames.Server::BoardGames.Server.Function::HandleAsync',
      code,
      environment: {
        TABLE_NAME: table.tableName,
        RESULTS_TABLE_NAME: resultsTable.tableName,
        PLAYERS_TABLE_NAME: playersTable.tableName,
        USER_POOL_ID: userPoolId,
        USER_POOL_CLIENT_ID: userPoolClientId,
      },
      logGroup,
      description: 'BOARD PIA online game server (WebSocket)',
    });
    table.grantReadWriteData(fn);
    resultsTable.grant(fn, 'dynamodb:PutItem'); // 結果は書き足すだけ（読む・消す権限は渡さない）
    // 戦績は読んで書き直す（結果と同時に TransactWriteItems で書く。中身は PutItem の権限で足りる）。消す権限は渡さない
    playersTable.grant(fn, 'dynamodb:Query', 'dynamodb:PutItem');

    // ---------- ランキング・プロフィール・マイページの API（ステップ 5-4） ----------
    // HTTP API ＋ Lambda。CloudFront（WebStack）の https://<domain>/api/* から呼ぶ（サイトと同じドメインなので CORS は要らない）
    const apiLogGroup = new logs.LogGroup(this, 'PlayerApiLogs', {
      retention: logs.RetentionDays.ONE_WEEK,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });
    const apiFn = new lambda.Function(this, 'PlayerApi', {
      runtime: lambda.Runtime.DOTNET_10,
      architecture: lambda.Architecture.ARM_64,
      memorySize: 1024, // 起動直後の準備を速くする（CPU はメモリに比例する。ステップ 6-2）
      timeout: cdk.Duration.seconds(10),
      handler: 'BoardGames.Server::BoardGames.Server.Api.ApiFunction::HandleAsync',
      code,
      environment: {
        PLAYERS_TABLE_NAME: playersTable.tableName,
        RESULTS_TABLE_NAME: resultsTable.tableName,
        USER_POOL_ID: userPoolId,
        USER_POOL_CLIENT_ID: userPoolClientId,
      },
      logGroup: apiLogGroup,
      description: 'BOARD PIA ranking / profile / my page API',
    });
    // 読む（ランキングの索引も）・名前を書く・アカウントの削除で消す
    playersTable.grant(apiFn, 'dynamodb:Query', 'dynamodb:BatchGetItem', 'dynamodb:PutItem', 'dynamodb:DeleteItem',
      'dynamodb:BatchWriteItem');
    // アカウントの削除: 対局結果の行から、その人の ID だけを取り除く
    resultsTable.grant(apiFn, 'dynamodb:UpdateItem');
    // アカウントの削除: ログインのユーザーを消す（このユーザープールだけ）
    apiFn.addToRolePolicy(new iam.PolicyStatement({
      actions: ['cognito-idp:AdminDeleteUser'],
      resources: [`arn:aws:cognito-idp:${this.region}:${this.account}:userpool/${userPoolId}`],
    }));

    const httpApi = new apigwv2.HttpApi(this, 'PlayerHttpApi', {
      apiName: 'boardpia-player-api',
      description: 'BOARD PIA ranking / profile / my page API (via CloudFront /api/*)',
      createDefaultStage: false,
    });
    httpApi.addRoutes({
      path: '/api/{proxy+}',
      // POST は CSP の違反の報告（/api/csp-report。ステップ 6-3）
      methods: [apigwv2.HttpMethod.GET, apigwv2.HttpMethod.PUT, apigwv2.HttpMethod.DELETE, apigwv2.HttpMethod.POST],
      integration: new HttpLambdaIntegration('PlayerApiIntegration', apiFn),
    });
    new apigwv2.HttpStage(this, 'PlayerApiStage', {
      httpApi,
      stageName: '$default',
      autoDeploy: true,
      // 費用の安全装置（ランキングは CloudFront が 60 秒覚えるので、ここまで届くのはごく一部）
      throttle: { rateLimit: 20, burstLimit: 40 },
    });
    // WebStack（GitHub Actions からデプロイ）が CloudFront の行き先に使う。出力ではなく SSM で渡す（GameStack は CI で読み込まないため）
    new ssm.StringParameter(this, 'ApiDomainParam', {
      parameterName: GameStack.apiDomainParam,
      stringValue: `${httpApi.apiId}.execute-api.${this.region}.amazonaws.com`,
    });
    new cdk.CfnOutput(this, 'PlayerApiUrl', { value: httpApi.apiEndpoint });

    // ---------- API Gateway（WebSocket） ----------
    const integration = (name: string) => new WebSocketLambdaIntegration(name, fn);
    const api = new apigwv2.WebSocketApi(this, 'GameApi', {
      apiName: 'boardpia-game',
      description: 'BOARD PIA online game server',
      connectRouteOptions: { integration: integration('ConnectIntegration') },
      disconnectRouteOptions: { integration: integration('DisconnectIntegration') },
      // メッセージは JSON ではない（"@quick|vanish" や "M|1|4"）ので、すべて $default に届く
      defaultRouteOptions: { integration: integration('MessageIntegration') },
    });

    const stage = new apigwv2.WebSocketStage(this, 'GameStage', {
      webSocketApi: api,
      stageName: 'prod',
      autoDeploy: true,
      // 費用の安全装置: 受け付けるメッセージの数に上限を付ける（いたずらで大量に送られても請求が膨らまない）。
      // 1 日 200 人なら平均 1 秒に 1 件ほどなので、十分な余裕がある。人が増えたら上げる
      throttle: { rateLimit: 50, burstLimit: 100 },
    });
    stage.grantManagementApiAccess(fn);
    fn.addEnvironment('CALLBACK_URL', stage.callbackUrl);

    // ---------- 独自ドメイン（wss://ws.boardgamepia.com） ----------
    const zone = route53.HostedZone.fromLookup(this, 'Zone', { domainName: props.domainName });
    const certificate = new acm.Certificate(this, 'WsCertificate', {
      domainName: wsDomain,
      validation: acm.CertificateValidation.fromDns(zone),
    });
    const domain = new apigwv2.DomainName(this, 'WsDomain', {
      domainName: wsDomain,
      certificate,
      securityPolicy: apigwv2.SecurityPolicy.TLS_1_2,
    });
    new apigwv2.ApiMapping(this, 'WsMapping', { api, domainName: domain, stage });
    new route53.ARecord(this, 'WsRecord', {
      zone,
      recordName: wsDomain,
      target: route53.RecordTarget.fromAlias(
        new route53targets.ApiGatewayv2DomainProperties(domain.regionalDomainName, domain.regionalHostedZoneId)),
    });

    new cdk.CfnOutput(this, 'WebSocketUrl', { value: `wss://${wsDomain}` });
    new cdk.CfnOutput(this, 'WebSocketStageUrl', { value: stage.url });
  }
}

/** フォルダの中のファイル（bin・obj を除く）の内容から、変わったかどうかを見分けるためのハッシュを作る */
function hashSources(dirs: string[]): string {
  const hash = crypto.createHash('sha256');
  const walk = (dir: string) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
      if (entry.name === 'bin' || entry.name === 'obj' || entry.name === 'TestResults') continue;
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else {
        hash.update(path.relative(dirs[0], full));
        hash.update(fs.readFileSync(full));
      }
    }
  };
  dirs.forEach(walk);
  return hash.digest('hex');
}
