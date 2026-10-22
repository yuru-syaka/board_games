import { execFileSync } from 'child_process';
import * as crypto from 'crypto';
import * as fs from 'fs';
import * as path from 'path';
import * as cdk from 'aws-cdk-lib/core';
import * as acm from 'aws-cdk-lib/aws-certificatemanager';
import * as apigwv2 from 'aws-cdk-lib/aws-apigatewayv2';
import { WebSocketLambdaIntegration } from 'aws-cdk-lib/aws-apigatewayv2-integrations';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as logs from 'aws-cdk-lib/aws-logs';
import * as route53 from 'aws-cdk-lib/aws-route53';
import * as route53targets from 'aws-cdk-lib/aws-route53-targets';
import { Construct } from 'constructs';

export interface GameStackProps extends cdk.StackProps {
  domainName: string;
}

/**
 * オンライン対戦サーバー（wss://ws.<domain>）。費用を抑えるため、常に動いているサーバーは使わない。
 *   API Gateway（WebSocket）… ブラウザとつながり続ける。メッセージが届いたときだけ Lambda を呼ぶ
 *   Lambda（C#、.NET 10、arm64）… server/BoardGames.Server。ルールは shared/ の Unity と同じコード
 *   DynamoDB（オンデマンド）… 接続・待ち行列・ルームコード・対局（種 ＋ 手の一覧）。ttl で自動的に消える
 *                              対局結果は別のテーブル（期限なし）。ステップ 5 のレーティング・ランキングの元
 * 費用の安全装置: API Gateway のスロットリング（1 秒あたりの上限）、ログの保存は 1 週間。
 * ビルド: cdk deploy GameStack のときに、手元の dotnet publish で Lambda のコードを作る（Docker は使わない）。
 * GitHub Actions からはデプロイしない（PlayStack と同じく手元の PC から）。
 */
export class GameStack extends cdk.Stack {
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
    });

    // ---------- Lambda ----------
    const logGroup = new logs.LogGroup(this, 'GameServerLogs', {
      retention: logs.RetentionDays.ONE_WEEK,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    const fn = new lambda.Function(this, 'GameServer', {
      runtime: lambda.Runtime.DOTNET_10,
      architecture: lambda.Architecture.ARM_64, // x86 より安い
      memorySize: 512,
      timeout: cdk.Duration.seconds(10),
      handler: 'BoardGames.Server::BoardGames.Server.Function::HandleAsync',
      code: lambda.Code.fromAsset(repoRoot, {
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
      }),
      environment: {
        TABLE_NAME: table.tableName,
        RESULTS_TABLE_NAME: resultsTable.tableName,
      },
      logGroup,
      description: 'BOARD PIA online game server (WebSocket)',
    });
    table.grantReadWriteData(fn);
    resultsTable.grant(fn, 'dynamodb:PutItem'); // 結果は書き足すだけ（読む・消す権限は渡さない）

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
