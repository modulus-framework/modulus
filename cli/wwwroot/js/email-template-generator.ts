/**
 * EmailTemplateGenerator - Generate professional email templates
 * Creates HTML emails for invoices, receipts, reports, notifications
 */

export interface EmailTemplate {
  name: string;
  type: 'invoice' | 'receipt' | 'notification' | 'report' | 'welcome' | 'reminder';
  subject: string;
  preheader?: string;
  body: string;
  footerText?: string;
  backgroundColor?: string;
  primaryColor?: string;
}

export interface EmailConfig {
  type: string;
  name: string;
  subject?: string;
  recipientEmail?: string;
  senderName?: string;
  senderEmail?: string;
  logoUrl?: string;
  brandColor?: string;
}

export class EmailTemplateGenerator {
  /**
   * Generate email template
   */
  static generateTemplate(config: EmailConfig): EmailTemplate {
    const templates: Record<string, any> = {
      'invoice': {
        subject: config.subject || 'Invoice #{{invoiceNumber}}',
        preheader: 'Invoice for {{clientName}} - Amount: {{amount}}',
        body: this.invoiceTemplate()
      },
      'receipt': {
        subject: config.subject || 'Receipt #{{transactionId}}',
        preheader: 'Thank you for your purchase',
        body: this.receiptTemplate()
      },
      'notification': {
        subject: config.subject || 'New Notification',
        preheader: 'You have a new notification',
        body: this.notificationTemplate()
      },
      'report': {
        subject: config.subject || '{{reportTitle}} Report',
        preheader: 'Your {{period}} report is ready',
        body: this.reportTemplate()
      },
      'welcome': {
        subject: config.subject || 'Welcome to {{companyName}}',
        preheader: 'Get started with your new account',
        body: this.welcomeTemplate()
      },
      'reminder': {
        subject: config.subject || 'Reminder: {{reminderTitle}}',
        preheader: 'You have an upcoming {{reminderType}}',
        body: this.reminderTemplate()
      }
    };

    const template = templates[config.type] || templates['notification'];

    return {
      name: config.name,
      type: config.type as any,
      subject: template.subject,
      preheader: template.preheader,
      body: template.body,
      backgroundColor: config.brandColor || '#f5f5f5',
      primaryColor: config.logoUrl || '#2c3e50'
    };
  }

  /**
   * Invoice email template
   */
  private static invoiceTemplate(): string {
    return `
<table width="100%" cellpadding="0" cellspacing="0" style="background: #f5f5f5; padding: 20px 0;">
  <tr>
    <td style="padding: 20px;">
      <table width="600" cellpadding="0" cellspacing="0" style="background: white; margin: 0 auto; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1);">
        <!-- Header -->
        <tr style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);">
          <td style="padding: 30px 20px; text-align: center;">
            <h1 style="color: white; margin: 0; font-size: 24px;">Invoice</h1>
          </td>
        </tr>

        <!-- Invoice Details -->
        <tr>
          <td style="padding: 30px 20px;">
            <p style="margin: 0 0 20px 0; color: #666;">
              <strong>Invoice #:</strong> {{invoiceNumber}}<br>
              <strong>Date:</strong> {{date}}<br>
              <strong>Due Date:</strong> {{dueDate}}
            </p>

            <h3 style="color: #2c3e50; margin: 20px 0 10px 0;">Bill To:</h3>
            <p style="margin: 0; color: #333;">
              {{clientName}}<br>
              {{clientEmail}}<br>
              {{clientAddress}}
            </p>
          </td>
        </tr>

        <!-- Items Table -->
        <tr>
          <td style="padding: 0 20px;">
            <table width="100%" cellpadding="10" cellspacing="0" style="border-collapse: collapse;">
              <tr style="background: #f9f9f9; border-bottom: 2px solid #ddd;">
                <th style="text-align: left; padding: 10px; color: #2c3e50;">Description</th>
                <th style="text-align: right; padding: 10px; color: #2c3e50;">Amount</th>
              </tr>
              <tr style="border-bottom: 1px solid #eee;">
                <td style="padding: 10px;">{{description}}</td>
                <td style="text-align: right; padding: 10px;">{{amount}}</td>
              </tr>
              <tr style="border-bottom: 1px solid #eee;">
                <td style="padding: 10px; text-align: right;">Subtotal:</td>
                <td style="text-align: right; padding: 10px;">{{subtotal}}</td>
              </tr>
              <tr style="border-bottom: 1px solid #eee;">
                <td style="padding: 10px; text-align: right;">Tax ({{taxRate}}%):</td>
                <td style="text-align: right; padding: 10px;">{{tax}}</td>
              </tr>
              <tr style="background: #2c3e50; color: white;">
                <td style="padding: 15px; text-align: right; font-weight: bold;">Total:</td>
                <td style="text-align: right; padding: 15px; font-weight: bold; font-size: 18px;">{{total}}</td>
              </tr>
            </table>
          </td>
        </tr>

        <!-- Call to Action -->
        <tr>
          <td style="padding: 20px; text-align: center;">
            <a href="{{paymentLink}}" style="display: inline-block; background: #667eea; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; font-weight: bold;">Pay Invoice</a>
          </td>
        </tr>

        <!-- Footer -->
        <tr style="background: #f9f9f9; border-top: 1px solid #ddd;">
          <td style="padding: 20px; text-align: center; color: #666; font-size: 12px;">
            <p style="margin: 0;">{{companyName}}</p>
            <p style="margin: 5px 0 0 0;">{{companyAddress}} | {{companyPhone}} | {{companyEmail}}</p>
          </td>
        </tr>
      </table>
    </td>
  </tr>
</table>`;
  }

  /**
   * Receipt email template
   */
  private static receiptTemplate(): string {
    return `
<table width="100%" cellpadding="0" cellspacing="0" style="background: #f5f5f5; padding: 20px 0;">
  <tr>
    <td style="padding: 20px;">
      <table width="600" cellpadding="0" cellspacing="0" style="background: white; margin: 0 auto; border-radius: 8px;">
        <!-- Header -->
        <tr style="background: linear-gradient(135deg, #27ae60 0%, #229954 100%);">
          <td style="padding: 30px 20px; text-align: center;">
            <h1 style="color: white; margin: 0; font-size: 24px;">✓ Receipt Confirmed</h1>
          </td>
        </tr>

        <!-- Content -->
        <tr>
          <td style="padding: 30px 20px;">
            <p style="color: #333; margin: 0 0 20px 0;">Thank you for your purchase!</p>

            <p style="color: #666; margin: 0 0 20px 0;">
              <strong>Transaction ID:</strong> {{transactionId}}<br>
              <strong>Date:</strong> {{date}}<br>
              <strong>Payment Method:</strong> {{paymentMethod}}
            </p>

            <table width="100%" cellpadding="10" cellspacing="0" style="border-collapse: collapse; margin: 20px 0;">
              <tr style="background: #f9f9f9; border-bottom: 2px solid #ddd;">
                <th style="text-align: left; padding: 10px; color: #2c3e50;">Item</th>
                <th style="text-align: right; padding: 10px; color: #2c3e50;">Price</th>
              </tr>
              <tr style="border-bottom: 1px solid #eee;">
                <td style="padding: 10px;">{{itemName}}</td>
                <td style="text-align: right; padding: 10px;">{{itemPrice}}</td>
              </tr>
              <tr style="background: #27ae60; color: white;">
                <td style="padding: 15px; text-align: right; font-weight: bold;">Total:</td>
                <td style="text-align: right; padding: 15px; font-weight: bold; font-size: 18px;">{{totalAmount}}</td>
              </tr>
            </table>

            <a href="{{receiptLink}}" style="display: inline-block; background: #27ae60; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; margin-top: 15px;">View Receipt</a>
          </td>
        </tr>

        <!-- Footer -->
        <tr style="background: #f9f9f9; border-top: 1px solid #ddd;">
          <td style="padding: 20px; text-align: center; color: #666; font-size: 12px;">
            <p style="margin: 0;">Questions? Contact us at {{supportEmail}}</p>
          </td>
        </tr>
      </table>
    </td>
  </tr>
</table>`;
  }

  /**
   * Welcome email template
   */
  private static welcomeTemplate(): string {
    return `
<table width="100%" cellpadding="0" cellspacing="0" style="background: #f5f5f5; padding: 20px 0;">
  <tr>
    <td style="padding: 20px;">
      <table width="600" cellpadding="0" cellspacing="0" style="background: white; margin: 0 auto; border-radius: 8px;">
        <tr style="background: linear-gradient(135deg, #3498db 0%, #2980b9 100%);">
          <td style="padding: 30px 20px; text-align: center;">
            <h1 style="color: white; margin: 0; font-size: 28px;">Welcome!</h1>
            <p style="color: white; margin: 10px 0 0 0; font-size: 16px;">We're excited to have you</p>
          </td>
        </tr>

        <tr>
          <td style="padding: 30px 20px;">
            <p style="color: #333; font-size: 16px; margin: 0 0 15px 0;">Hi {{firstName}},</p>
            <p style="color: #555; line-height: 1.6; margin: 0 0 20px 0;">
              Welcome to {{companyName}}! We're thrilled to have you join our community.
              Your account is now active and ready to use.
            </p>

            <h3 style="color: #2c3e50; margin: 20px 0 15px 0;">Getting Started:</h3>
            <ul style="color: #555; line-height: 1.8;">
              <li>Verify your email address</li>
              <li>Complete your profile</li>
              <li>Explore our features</li>
              <li>Join the community</li>
            </ul>

            <a href="{{getStartedLink}}" style="display: inline-block; background: #3498db; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; font-weight: bold; margin-top: 20px;">Get Started</a>
          </td>
        </tr>

        <tr style="background: #f9f9f9; border-top: 1px solid #ddd;">
          <td style="padding: 20px; text-align: center; color: #666; font-size: 12px;">
            <p style="margin: 0;">{{companyName}} | {{supportEmail}}</p>
          </td>
        </tr>
      </table>
    </td>
  </tr>
</table>`;
  }

  /**
   * Notification template
   */
  private static notificationTemplate(): string {
    return `<table width="100%" cellpadding="0" cellspacing="0" style="background: #f5f5f5; padding: 20px 0;">
  <tr><td style="padding: 20px;"><table width="600" cellpadding="0" cellspacing="0" style="background: white; margin: 0 auto; border-radius: 8px;">
    <tr style="background: #f39c12;"><td style="padding: 30px 20px; text-align: center;"><h2 style="color: white; margin: 0;">{{notificationTitle}}</h2></td></tr>
    <tr><td style="padding: 30px 20px;"><p style="color: #333; margin: 0 0 15px 0;">{{notificationMessage}}</p>
    <a href="{{actionLink}}" style="display: inline-block; background: #f39c12; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;">View Details</a></td></tr>
    <tr style="background: #f9f9f9; border-top: 1px solid #ddd;"><td style="padding: 20px; text-align: center; color: #666; font-size: 12px;"><p style="margin: 0;">{{companyName}}</p></td></tr>
  </table></td></tr>
</table>`;
  }

  /**
   * Report template
   */
  private static reportTemplate(): string {
    return `<table width="100%" cellpadding="0" cellspacing="0" style="background: #f5f5f5; padding: 20px 0;">
  <tr><td style="padding: 20px;"><table width="600" cellpadding="0" cellspacing="0" style="background: white; margin: 0 auto; border-radius: 8px;">
    <tr style="background: #34495e;"><td style="padding: 30px 20px; text-align: center;"><h1 style="color: white; margin: 0;">{{reportTitle}}</h1><p style="color: white; margin: 5px 0 0 0;">{{period}}</p></td></tr>
    <tr><td style="padding: 30px 20px;"><h3 style="color: #2c3e50;">Summary:</h3><p style="color: #555; line-height: 1.6;">{{reportSummary}}</p>
    <h3 style="color: #2c3e50; margin-top: 20px;">Key Metrics:</h3><p style="color: #555;"><strong>Total:</strong> {{totalMetric}}<br><strong>Average:</strong> {{averageMetric}}<br><strong>Growth:</strong> {{growthMetric}}</p>
    <a href="{{reportLink}}" style="display: inline-block; background: #34495e; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; margin-top: 20px;">View Full Report</a></td></tr>
    <tr style="background: #f9f9f9; border-top: 1px solid #ddd;"><td style="padding: 20px; text-align: center; color: #666; font-size: 12px;"><p style="margin: 0;">{{companyName}}</p></td></tr>
  </table></td></tr>
</table>`;
  }

  /**
   * Reminder template
   */
  private static reminderTemplate(): string {
    return `<table width="100%" cellpadding="0" cellspacing="0" style="background: #f5f5f5; padding: 20px 0;">
  <tr><td style="padding: 20px;"><table width="600" cellpadding="0" cellspacing="0" style="background: white; margin: 0 auto; border-radius: 8px;">
    <tr style="background: #e74c3c;"><td style="padding: 30px 20px; text-align: center;"><h2 style="color: white; margin: 0;">⏰ Reminder</h2></td></tr>
    <tr><td style="padding: 30px 20px;"><p style="color: #333; margin: 0 0 20px 0;"><strong>{{reminderTitle}}</strong></p>
    <p style="color: #555; line-height: 1.6;">{{reminderDescription}}</p>
    <p style="color: #666; margin: 20px 0 0 0;"><strong>Scheduled for:</strong> {{reminderDate}} at {{reminderTime}}</p>
    <a href="{{actionLink}}" style="display: inline-block; background: #e74c3c; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; margin-top: 20px;">Take Action</a></td></tr>
    <tr style="background: #f9f9f9; border-top: 1px solid #ddd;"><td style="padding: 20px; text-align: center; color: #666; font-size: 12px;"><p style="margin: 0;">{{companyName}}</p></td></tr>
  </table></td></tr>
</table>`;
  }

  /**
   * Export template as HTML
   */
  static exportHTML(template: EmailTemplate): string {
    return `<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>${template.subject}</title>
</head>
<body style="margin: 0; padding: 0; background: ${template.backgroundColor};">
  ${template.body}
</body>
</html>`;
  }

  /**
   * Export template as JSON for database storage
   */
  static exportJSON(template: EmailTemplate): string {
    return JSON.stringify(template, null, 2);
  }
}
