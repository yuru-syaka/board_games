import * as cdk from 'aws-cdk-lib/core';
import * as acm from 'aws-cdk-lib/aws-certificatemanager';
import * as route53 from 'aws-cdk-lib/aws-route53';
import { Construct } from 'constructs';

export interface CertStackProps extends cdk.StackProps {
  /** Route 53 で購入したドメイン（ホストゾーンは購入時に自動で作られたものを使う） */
  domainName: string;
}

/**
 * CloudFront 用の SSL 証明書。CloudFront は us-east-1 の証明書しか使えないため、このスタックは us-east-1 に置く。
 * 検証用の DNS レコードは Route 53 に自動で追加される（ACM の証明書は無料、更新も自動）。
 */
export class CertStack extends cdk.Stack {
  readonly certificate: acm.ICertificate;
  /** ログイン画面（auth.<domain>、Cognito）用。Cognito の独自ドメインも us-east-1 の証明書しか使えない */
  readonly authCertificate: acm.ICertificate;

  constructor(scope: Construct, id: string, props: CertStackProps) {
    super(scope, id, props);

    const zone = route53.HostedZone.fromLookup(this, 'Zone', { domainName: props.domainName });

    this.certificate = new acm.Certificate(this, 'SiteCertificate', {
      domainName: props.domainName,
      subjectAlternativeNames: [`www.${props.domainName}`],
      validation: acm.CertificateValidation.fromDns(zone),
    });

    this.authCertificate = new acm.Certificate(this, 'AuthCertificate', {
      domainName: `auth.${props.domainName}`,
      validation: acm.CertificateValidation.fromDns(zone),
    });

    new cdk.CfnOutput(this, 'CertificateArn', { value: this.certificate.certificateArn });
  }
}
