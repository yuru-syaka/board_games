#!/usr/bin/env node
import * as fs from 'fs';
import * as path from 'path';
import * as cdk from 'aws-cdk-lib/core';
import { CertStack } from '../lib/cert-stack';
import { AuthStack } from '../lib/auth-stack';
import { WebStack } from '../lib/web-stack';
import { SecurityStack } from '../lib/security-stack';
import { MonitoringStack } from '../lib/monitoring-stack';
import { PlayStack } from '../lib/play-stack';
import { GameStack } from '../lib/game-stack';
import { GitHubDeployStack } from '../lib/github-deploy-stack';

const app = new cdk.App();

// Account comes from the CLI profile (--profile board-game-pia-admin).
const account = process.env.CDK_DEFAULT_ACCOUNT;
const tokyo = { account, region: 'ap-northeast-1' };
const usEast1 = { account, region: 'us-east-1' }; // CloudFront のメトリクスと証明書は us-east-1

// Route 53 で購入したドメイン
const domainName = 'boardgamepia.com';

// 通知先メールアドレスは公開リポジトリに書かない。infra/config.local.json（git 管理外）か環境変数 ALERT_EMAIL で渡す
function alertEmail(): string {
  // 前後の空白や BOM（見えない文字）を取り除く。混ざると別のアドレスとして購読されてしまう
  const clean = (s: string) => s.replace(/^\uFEFF/, '').trim();
  if (process.env.ALERT_EMAIL) return clean(process.env.ALERT_EMAIL);
  const file = path.join(__dirname, '..', 'config.local.json');
  if (fs.existsSync(file)) return clean(JSON.parse(fs.readFileSync(file, 'utf8')).alertEmail);
  throw new Error('通知先が未設定です。infra/config.local.json に {"alertEmail": "..."} を書くか、ALERT_EMAIL を設定してください。');
}
const email = alertEmail();

const cert = new CertStack(app, 'CertStack', { env: usEast1, crossRegionReferences: true, domainName });

// ログイン（Cognito）。ログイン画面 auth.<domain> の証明書は CertStack（us-east-1）から受け取る。
// Google ログインの ID とシークレットは GitHub Actions の Secrets から環境変数で受け取る（公開リポジトリなので書かない）
const googleClientId = process.env.GOOGLE_CLIENT_ID?.trim();
const googleClientSecret = process.env.GOOGLE_CLIENT_SECRET?.trim();
const auth = new AuthStack(app, 'AuthStack', {
  env: tokyo,
  crossRegionReferences: true,
  domainName,
  authCertificate: cert.authCertificate,
  google: googleClientId && googleClientSecret ? { clientId: googleClientId, clientSecret: googleClientSecret } : undefined,
});

const web = new WebStack(app, 'WebStack', {
  env: tokyo,
  crossRegionReferences: true,
  domainName,
  certificate: cert.certificate,
  auth: { domain: auth.authDomain, clientId: auth.client.userPoolClientId },
});

// ゲームの WebGL ビルド（git 管理外）。手元の PC からだけ cdk deploy PlayStack で反映する
const play = new PlayStack(app, 'PlayStack', { env: tokyo });
play.addDependency(web); // WebStack が SSM パラメータを書いてから PlayStack を作る

// オンライン対戦サーバー（API Gateway WebSocket ＋ Lambda ＋ DynamoDB）。手元の PC からだけ cdk deploy GameStack で反映する
// （Lambda のコードを手元の dotnet publish で作るため。GitHub Actions には .NET 10 がなく、デプロイもしないので読み込まない）
if (!process.env.GITHUB_ACTIONS) new GameStack(app, 'GameStack', { env: tokyo, domainName });

new SecurityStack(app, 'SecurityStack', { env: tokyo, alertEmail: email });

// GitHub Actions（OIDC）からデプロイするためのロール。手元の PC からだけ更新する
new GitHubDeployStack(app, 'GitHubDeployStack', {
  env: tokyo,
  repository: 'yuru-syaka/board_games',
  immutableRepository: 'yuru-syaka@339159024/board_games@1415315348', // GitHub の immutable subject 形式
  branch: 'main',
});

new MonitoringStack(app, 'MonitoringStack', {
  env: usEast1,
  crossRegionReferences: true,
  alertEmail: email,
  distributionId: web.distribution.distributionId,
});
