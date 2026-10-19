import * as cdk from 'aws-cdk-lib/core';
import * as iam from 'aws-cdk-lib/aws-iam';
import { Construct } from 'constructs';

export interface GitHubDeployStackProps extends cdk.StackProps {
  /** デプロイを許可する GitHub リポジトリ（owner/name） */
  repository: string;
  /**
   * 同じリポジトリの、ID 付きの表記（owner@ownerId/name@repoId）。
   * GitHub の「immutable subject」が有効なリポジトリでは、トークンの sub がこの形式になる。
   * 確認方法: gh api repos/<owner>/<name>/actions/oidc/customization/sub の sub_claim_prefix
   */
  immutableRepository?: string;
  /** デプロイを許可するブランチ */
  branch: string;
}

/**
 * GitHub Actions から AWS にデプロイするための OIDC 接続。
 * - アクセスキーは使わない。GitHub が実行ごとに発行するトークン（身分証明書）を AWS が確認し、
 *   1 時間ほどで失効する一時的な鍵を渡す。
 * - 使えるのは指定したリポジトリ・ブランチ（push で動くワークフロー）だけ。フォークや別ブランチからは使えない。
 * - このロールにできるのは「CDK の bootstrap ロールを引き受けること」だけ。
 *   実際のリソースの作成・変更は、CDK の仕組みどおり CloudFormation 経由で行う。
 * - このスタック自体は GitHub Actions からは更新しない（手元から cdk deploy GitHubDeployStack）。
 */
export class GitHubDeployStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props: GitHubDeployStackProps) {
    super(scope, id, props);

    // 「GitHub Actions が発行したトークンを信用する」という登録（アカウントに 1 つ）
    const provider = new iam.OidcProviderNative(this, 'GitHubOidc', {
      url: 'https://token.actions.githubusercontent.com',
      clientIds: ['sts.amazonaws.com'],
    });

    const role = new iam.Role(this, 'DeployRole', {
      roleName: 'GitHubActionsDeployRole',
      // IAM の説明文は英数字と一部の記号しか使えないため英語で書く
      description: `cdk deploy from GitHub Actions (${props.repository}@${props.branch}) via OIDC`,
      maxSessionDuration: cdk.Duration.hours(1),
      assumedBy: new iam.WebIdentityPrincipal(provider.oidcProviderArn, {
        StringEquals: {
          'token.actions.githubusercontent.com:aud': 'sts.amazonaws.com',
          // repo:<リポジトリ>:ref:refs/heads/<ブランチ> に完全一致するトークンだけを受け付ける
          //（従来の表記と、ID 付きの表記のどちらでもよい）
          'token.actions.githubusercontent.com:sub': [props.repository, props.immutableRepository]
            .filter((r): r is string => !!r)
            .map((r) => `repo:${r}:ref:refs/heads/${props.branch}`),
        },
      }),
    });

    // CDK の bootstrap ロール（deploy / file-publishing / image-publishing / lookup）を、全リージョン分引き受けられる
    role.addToPolicy(new iam.PolicyStatement({
      sid: 'AssumeCdkBootstrapRoles',
      actions: ['sts:AssumeRole', 'sts:TagSession'],
      resources: [`arn:aws:iam::${this.account}:role/cdk-hnb659fds-*-${this.account}-*`],
    }));

    new cdk.CfnOutput(this, 'DeployRoleArn', { value: role.roleArn });
  }
}
