#!/usr/bin/env node
import * as fs from 'fs';
import * as path from 'path';
import * as cdk from 'aws-cdk-lib/core';
import { CertStack } from '../lib/cert-stack';
import { WebStack } from '../lib/web-stack';
import { SecurityStack } from '../lib/security-stack';
import { MonitoringStack } from '../lib/monitoring-stack';
import { PlayStack } from '../lib/play-stack';
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
  if (process.env.ALERT_EMAIL) return process.env.ALERT_EMAIL;
  const file = path.join(__dirname, '..', 'config.local.json');
  if (fs.existsSync(file)) return JSON.parse(fs.readFileSync(file, 'utf8')).alertEmail;
  throw new Error('通知先が未設定です。infra/config.local.json に {"alertEmail": "..."} を書くか、ALERT_EMAIL を設定してください。');
}
const email = alertEmail();

const cert = new CertStack(app, 'CertStack', { env: usEast1, crossRegionReferences: true, domainName });

const web = new WebStack(app, 'WebStack', {
  env: tokyo,
  crossRegionReferences: true,
  domainName,
  certificate: cert.certificate,
});

// ゲームの WebGL ビルド（git 管理外）。手元の PC からだけ cdk deploy PlayStack で反映する
const play = new PlayStack(app, 'PlayStack', { env: tokyo });
play.addDependency(web); // WebStack が SSM パラメータを書いてから PlayStack を作る

new SecurityStack(app, 'SecurityStack', { env: tokyo, alertEmail: email });

// GitHub Actions（OIDC）からデプロイするためのロール。手元の PC からだけ更新する
new GitHubDeployStack(app, 'GitHubDeployStack', {
  env: tokyo,
  repository: 'yuru-syaka/board_games',
  branch: 'main',
});

new MonitoringStack(app, 'MonitoringStack', {
  env: usEast1,
  crossRegionReferences: true,
  alertEmail: email,
  distributionId: web.distribution.distributionId,
});
