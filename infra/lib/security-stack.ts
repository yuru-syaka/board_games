import * as cdk from 'aws-cdk-lib/core';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as sns from 'aws-cdk-lib/aws-sns';
import * as subs from 'aws-cdk-lib/aws-sns-subscriptions';
import * as cloudtrail from 'aws-cdk-lib/aws-cloudtrail';
import * as guardduty from 'aws-cdk-lib/aws-guardduty';
import * as accessanalyzer from 'aws-cdk-lib/aws-accessanalyzer';
import * as events from 'aws-cdk-lib/aws-events';
import * as targets from 'aws-cdk-lib/aws-events-targets';
import { Construct } from 'constructs';

export interface SecurityStackProps extends cdk.StackProps {
  /** セキュリティ通知の送り先（リポジトリには書かず、config.local.json か環境変数から渡す） */
  alertEmail: string;
}

/**
 * アカウント全体の安全対策（東京リージョン）。
 * - CloudTrail: 全リージョンの管理イベントを S3 に保存（改ざん検出付き）
 * - GuardDuty: 不審な動きの検知。重要度「中」以上をメールで通知
 * - IAM Access Analyzer: 外部に公開・共有されているリソースの検出
 */
export class SecurityStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props: SecurityStackProps) {
    super(scope, id, props);

    const alerts = new sns.Topic(this, 'SecurityAlerts', {
      displayName: 'BOARD PIA security alerts',
      enforceSSL: true,
    });
    alerts.addSubscription(new subs.EmailSubscription(props.alertEmail));

    // ---------- CloudTrail ----------
    const trailBucket = new s3.Bucket(this, 'TrailBucket', {
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      encryption: s3.BucketEncryption.S3_MANAGED,
      enforceSSL: true,
      // 監査ログはスタックを消しても残す。保存料を抑えるため 1 年で削除する
      removalPolicy: cdk.RemovalPolicy.RETAIN,
      lifecycleRules: [{ expiration: cdk.Duration.days(365) }],
    });

    new cloudtrail.Trail(this, 'ManagementTrail', {
      trailName: 'management-trail',
      bucket: trailBucket,
      isMultiRegionTrail: true,
      includeGlobalServiceEvents: true,
      enableFileValidation: true,
      managementEvents: cloudtrail.ReadWriteType.ALL,
      // CloudWatch Logs への転送は有料なので使わない（必要になったら有効にする）
      sendToCloudWatchLogs: false,
    });

    // ---------- GuardDuty ----------
    // 費用を抑えるため、基本の検知（CloudTrail・VPC フローログ・DNS ログ）と S3 保護だけを有効にする
    const off = ['EKS_AUDIT_LOGS', 'EBS_MALWARE_PROTECTION', 'RDS_LOGIN_EVENTS', 'RUNTIME_MONITORING', 'LAMBDA_NETWORK_LOGS'];
    new guardduty.CfnDetector(this, 'GuardDuty', {
      enable: true,
      findingPublishingFrequency: 'ONE_HOUR',
      features: [
        { name: 'S3_DATA_EVENTS', status: 'ENABLED' },
        ...off.map((name) => ({ name, status: 'DISABLED' })),
      ],
    });

    new events.Rule(this, 'GuardDutyFindingRule', {
      description: 'GuardDuty findings with severity >= 4 (Medium) to email',
      eventPattern: {
        source: ['aws.guardduty'],
        detailType: ['GuardDuty Finding'],
        detail: { severity: events.Match.greaterThanOrEqual(4) },
      },
      targets: [
        new targets.SnsTopic(alerts, {
          message: events.RuleTargetInput.fromText(
            `[GuardDuty] 重要度 ${events.EventField.fromPath('$.detail.severity')}: ` +
              `${events.EventField.fromPath('$.detail.title')}\n` +
              `種類: ${events.EventField.fromPath('$.detail.type')}\n` +
              `リージョン: ${events.EventField.fromPath('$.region')}\n` +
              `詳細: ${events.EventField.fromPath('$.detail.description')}`,
          ),
        }),
      ],
    });

    // ---------- IAM Access Analyzer（外部アクセスの分析は無料） ----------
    new accessanalyzer.CfnAnalyzer(this, 'AccessAnalyzer', {
      analyzerName: 'account-external-access',
      type: 'ACCOUNT',
    });

    new cdk.CfnOutput(this, 'AlertTopicArn', { value: alerts.topicArn });
    new cdk.CfnOutput(this, 'TrailBucketName', { value: trailBucket.bucketName });
  }
}
