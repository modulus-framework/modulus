/**
 * Email Service - Send emails with templates
 * Supports multiple email providers (SendGrid, Mailgun, nodemailer)
 */

export interface EmailTemplate {
  name: string;
  subject: string;
  htmlBody: string;
  textBody?: string;
  from?: string;
  replyTo?: string;
}

export interface EmailMessage {
  to: string | string[];
  subject: string;
  html: string;
  text?: string;
  from: string;
  cc?: string[];
  bcc?: string[];
  attachments?: Array<{
    filename: string;
    content: Buffer | string;
    contentType?: string;
  }>;
}

export interface EmailProvider {
  type: 'sendgrid' | 'mailgun' | 'nodemailer' | 'mock';
  apiKey?: string;
  domain?: string;
  host?: string;
  port?: number;
  auth?: {
    user: string;
    pass: string;
  };
}

export interface SendResult {
  success: boolean;
  messageId?: string;
  error?: string;
  timestamp: Date;
}

export class EmailService {
  private static provider: EmailProvider | null = null;
  private static nodemailerTransporter: any = null;

  /**
   * Configure email provider
   */
  static configure(provider: EmailProvider): void {
    this.provider = provider;

    if (provider.type === 'nodemailer') {
      try {
        const nodemailer = require('nodemailer');
        this.nodemailerTransporter = nodemailer.createTransport({
          host: provider.host,
          port: provider.port,
          auth: provider.auth
        });
      } catch (error) {
        throw new Error('nodemailer not installed. Install with: npm install nodemailer');
      }
    }
  }

  /**
   * Send email with template
   */
  static async send(
    template: EmailTemplate,
    recipient: string | string[],
    variables?: Record<string, any>
  ): Promise<SendResult> {
    if (!this.provider) {
      return {
        success: false,
        error: 'Email provider not configured',
        timestamp: new Date()
      };
    }

    try {
      const recipients = Array.isArray(recipient) ? recipient : [recipient];
      const html = this.renderTemplate(template.htmlBody, variables);
      const text = template.textBody ? this.renderTemplate(template.textBody, variables) : undefined;
      const subject = this.renderTemplate(template.subject, variables);

      const message: EmailMessage = {
        to: recipients,
        subject,
        html,
        text,
        from: template.from || 'noreply@example.com'
      };

      return await this.sendMessage(message);
    } catch (error) {
      return {
        success: false,
        error: String(error),
        timestamp: new Date()
      };
    }
  }

  /**
   * Send email message
   */
  static async sendMessage(message: EmailMessage): Promise<SendResult> {
    if (!this.provider) {
      return {
        success: false,
        error: 'Email provider not configured',
        timestamp: new Date()
      };
    }

    try {
      switch (this.provider.type) {
        case 'sendgrid':
          return await this.sendViaSendGrid(message);
        case 'mailgun':
          return await this.sendViaMailgun(message);
        case 'nodemailer':
          return await this.sendViaNodemailer(message);
        case 'mock':
          return this.sendMock(message);
        default:
          return {
            success: false,
            error: 'Unknown provider type',
            timestamp: new Date()
          };
      }
    } catch (error) {
      return {
        success: false,
        error: String(error),
        timestamp: new Date()
      };
    }
  }

  /**
   * Send batch emails
   */
  static async sendBatch(
    template: EmailTemplate,
    recipients: string[],
    variables?: Record<string, any>
  ): Promise<SendResult[]> {
    const results: SendResult[] = [];

    for (const recipient of recipients) {
      const result = await this.send(template, recipient, variables);
      results.push(result);

      await this.delay(100);
    }

    return results;
  }

  /**
   * Send batch with personalization
   */
  static async sendBatchPersonalized(
    template: EmailTemplate,
    messages: Array<{
      recipient: string;
      variables?: Record<string, any>;
    }>
  ): Promise<SendResult[]> {
    const results: SendResult[] = [];

    for (const msg of messages) {
      const result = await this.send(template, msg.recipient, msg.variables);
      results.push(result);

      await this.delay(100);
    }

    return results;
  }

  /**
   * Send via SendGrid
   */
  private static async sendViaSendGrid(message: EmailMessage): Promise<SendResult> {
    try {
      const sgMail = require('@sendgrid/mail');
      sgMail.setApiKey(this.provider!.apiKey);

      const response = await sgMail.send({
        to: message.to,
        from: message.from,
        subject: message.subject,
        html: message.html,
        text: message.text,
        cc: message.cc,
        bcc: message.bcc
      });

      return {
        success: true,
        messageId: response[0].headers['x-message-id'],
        timestamp: new Date()
      };
    } catch (error) {
      throw error;
    }
  }

  /**
   * Send via Mailgun
   */
  private static async sendViaMailgun(message: EmailMessage): Promise<SendResult> {
    try {
      const Mailgun = require('mailgun.js');
      const FormData = require('form-data');

      const mg = new Mailgun(FormData);
      const client = mg.client({ username: 'api', key: this.provider!.apiKey });

      const response = await client.messages.create(this.provider!.domain || 'example.com', {
        from: message.from,
        to: Array.isArray(message.to) ? message.to : [message.to],
        subject: message.subject,
        html: message.html,
        text: message.text
      });

      return {
        success: true,
        messageId: response.id,
        timestamp: new Date()
      };
    } catch (error) {
      throw error;
    }
  }

  /**
   * Send via Nodemailer
   */
  private static async sendViaNodemailer(message: EmailMessage): Promise<SendResult> {
    if (!this.nodemailerTransporter) {
      throw new Error('Nodemailer not configured');
    }

    const response = await this.nodemailerTransporter.sendMail({
      from: message.from,
      to: Array.isArray(message.to) ? message.to.join(',') : message.to,
      subject: message.subject,
      html: message.html,
      text: message.text,
      cc: message.cc?.join(','),
      bcc: message.bcc?.join(','),
      attachments: message.attachments
    });

    return {
      success: true,
      messageId: response.messageId,
      timestamp: new Date()
    };
  }

  /**
   * Mock send (for testing)
   */
  private static sendMock(message: EmailMessage): SendResult {
    console.log('📧 Mock Email Sent:', {
      to: message.to,
      subject: message.subject,
      from: message.from
    });

    return {
      success: true,
      messageId: `mock-${Date.now()}`,
      timestamp: new Date()
    };
  }

  /**
   * Render template with variables
   */
  private static renderTemplate(template: string, variables?: Record<string, any>): string {
    if (!variables) return template;

    let result = template;

    Object.entries(variables).forEach(([key, value]) => {
      const regex = new RegExp(`{{${key}}}`, 'g');
      result = result.replace(regex, String(value || ''));
    });

    return result;
  }

  /**
   * Delay utility
   */
  private static delay(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
  }

  /**
   * Test connection
   */
  static async testConnection(): Promise<boolean> {
    if (!this.provider) return false;

    try {
      if (this.provider.type === 'nodemailer' && this.nodemailerTransporter) {
        await this.nodemailerTransporter.verify();
        return true;
      }
      return true;
    } catch {
      return false;
    }
  }

  /**
   * Get provider info
   */
  static getProviderInfo(): EmailProvider | null {
    return this.provider;
  }

  /**
   * Reset provider
   */
  static reset(): void {
    this.provider = null;
    this.nodemailerTransporter = null;
  }
}
