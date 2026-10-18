import * as cdk from 'aws-cdk-lib/core';
import * as sns from 'aws-cdk-lib/aws-sns';
import * as subs from 'aws-cdk-lib/aws-sns-subscriptions';
import * as cloudwatch from 'aws-cdk-lib/aws-cloudwatch';
import * as actions from 'aws-cdk-lib/aws-cloudwatch-actions';
import { Construct } from 'constructs';

export interface MonitoringStackProps extends cdk.StackProps {
  alertEmail: string;
  /** 監視する CloudFront ディストリビューションの ID（WebStack から受け取る） */
  distributionId: string;
}

/**
 * CloudFront の監視。CloudFront のメトリクスは us-east-1 にしか出ないため、このスタックは us-east-1 に置く。
 * CloudWatch のアラームは 10 個まで無料。
 */
export class MonitoringStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props: MonitoringStackProps) {
    super(scope, id, props);

    const alerts = new sns.Topic(this, 'MonitoringAlerts', {
      displayName: 'BOARD PIA monitoring alerts',
      enforceSSL: true,
    });
    alerts.addSubscription(new subs.EmailSubscription(props.alertEmail));
    const notify = new actions.SnsAction(alerts);

    const dims = { DistributionId: props.distributionId, Region: 'Global' };

    // サイトが壊れている（S3 のファイルがない、設定ミスなど）
    const errors5xx = new cloudwatch.Alarm(this, 'CloudFront5xxRate', {
      alarmDescription: 'CloudFront の 5xx エラー率が 15 分間 5% を超えた',
      metric: new cloudwatch.Metric({
        namespace: 'AWS/CloudFront',
        metricName: '5xxErrorRate',
        dimensionsMap: dims,
        statistic: 'Average',
        period: cdk.Duration.minutes(5),
      }),
      threshold: 5,
      evaluationPeriods: 3,
      comparisonOperator: cloudwatch.ComparisonOperator.GREATER_THAN_THRESHOLD,
      treatMissingData: cloudwatch.TreatMissingData.NOT_BREACHING,
    });

    // 想定外の大量アクセス（無料枠 1TB/月 ≒ 33GB/日 を超えるペース）
    const traffic = new cloudwatch.Alarm(this, 'CloudFrontDailyTraffic', {
      alarmDescription: 'CloudFront の配信量が 1 日で 50GB を超えた（無料枠を超えるペース）',
      metric: new cloudwatch.Metric({
        namespace: 'AWS/CloudFront',
        metricName: 'BytesDownloaded',
        dimensionsMap: dims,
        statistic: 'Sum',
        period: cdk.Duration.days(1),
      }),
      threshold: 50 * 1024 ** 3,
      evaluationPeriods: 1,
      comparisonOperator: cloudwatch.ComparisonOperator.GREATER_THAN_THRESHOLD,
      treatMissingData: cloudwatch.TreatMissingData.NOT_BREACHING,
    });

    // 短時間のアクセス集中（いたずら・攻撃の兆候）
    const requests = new cloudwatch.Alarm(this, 'CloudFrontRequestSpike', {
      alarmDescription: 'CloudFront へのリクエストが 5 分間で 50,000 件を超えた',
      metric: new cloudwatch.Metric({
        namespace: 'AWS/CloudFront',
        metricName: 'Requests',
        dimensionsMap: dims,
        statistic: 'Sum',
        period: cdk.Duration.minutes(5),
      }),
      threshold: 50_000,
      evaluationPeriods: 1,
      comparisonOperator: cloudwatch.ComparisonOperator.GREATER_THAN_THRESHOLD,
      treatMissingData: cloudwatch.TreatMissingData.NOT_BREACHING,
    });

    for (const alarm of [errors5xx, traffic, requests]) {
      alarm.addAlarmAction(notify);
      alarm.addOkAction(notify);
    }
  }
}
