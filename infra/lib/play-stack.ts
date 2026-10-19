import * as fs from 'fs';
import * as path from 'path';
import * as cdk from 'aws-cdk-lib/core';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as s3deploy from 'aws-cdk-lib/aws-s3-deployment';
import * as cloudfront from 'aws-cdk-lib/aws-cloudfront';
import * as ssm from 'aws-cdk-lib/aws-ssm';
import { Construct } from 'constructs';
import { WebStack } from './web-stack';

/**
 * ゲームの WebGL ビルド（web/play/<slug>/）を S3 の play/<slug>/ に置くスタック。
 * - ビルドは git に入れていないので、手元の PC から cdk deploy PlayStack で反映する。
 *   GitHub Actions からは更新しない（CI にはビルドがないため）。
 * - 手元にあるゲームだけをアップロードする。ないゲームは S3 から消さない
 *   （そのゲームのデプロイを作らないだけ。BucketDeployment は削除時にファイルを残す）。
 * - prune（不要ファイルの削除）はゲームのフォルダの中だけに効くので、古いビルドのファイルは消える。
 * - サイト用バケットと CloudFront は、WebStack が SSM パラメータに書いた値から読む
 *   （WebStack を直接参照すると、ビルドのない環境で WebStack をデプロイしたときに受け渡しが消えるため）。
 */
export class PlayStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const playDir = path.join(__dirname, '..', '..', 'web', 'play');
    const games = fs.existsSync(playDir)
      ? fs.readdirSync(playDir).filter((slug) => fs.existsSync(path.join(playDir, slug, 'index.html')))
      : [];

    if (games.length > 0) {
      const bucket = s3.Bucket.fromBucketName(this, 'SiteBucket',
        ssm.StringParameter.valueForStringParameter(this, WebStack.bucketNameParam));
      const distribution = cloudfront.Distribution.fromDistributionAttributes(this, 'SiteDistribution', {
        distributionId: ssm.StringParameter.valueForStringParameter(this, WebStack.distributionIdParam),
        domainName: 'boardgamepia.com', // 使わないが必須の項目
      });

      for (const slug of games) {
        new s3deploy.BucketDeployment(this, `DeployPlay-${slug}`, {
          sources: [s3deploy.Source.asset(path.join(playDir, slug), { exclude: ['unity-build.provenance.json'] })],
          destinationBucket: bucket,
          destinationKeyPrefix: `play/${slug}/`,
          memoryLimit: 1024, // Unity の WebGL ビルド（数十 MB）を展開するため
          distribution,
          distributionPaths: [`/play/${slug}/*`],
        });
      }
    }

    new cdk.CfnOutput(this, 'Games', { value: games.length ? games.join(', ') : '(none)' });
  }
}
