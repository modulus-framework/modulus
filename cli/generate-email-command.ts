#!/usr/bin/env node

/**
 * Modulus CLI - Email Template Generator Command
 * Usage: modulus generate email --type <type> --name <name> [options]
 */

import * as fs from 'fs';
import * as path from 'path';

interface EmailGenerateCommand {
  type: string;
  name: string;
  subject?: string;
  senderName?: string;
  senderEmail?: string;
  companyName?: string;
  brandColor?: string;
  logoUrl?: string;
  outputDir?: string;
}

function parseArgs(): EmailGenerateCommand {
  const args = process.argv.slice(2);
  const cmd: EmailGenerateCommand = {
    type: 'notification',
    name: 'email-template',
    outputDir: './'
  };

  for (let i = 0; i < args.length; i++) {
    switch (args[i]) {
      case '--type':
        cmd.type = args[++i];
        break;
      case '--name':
        cmd.name = args[++i];
        break;
      case '--subject':
        cmd.subject = args[++i];
        break;
      case '--sender-name':
        cmd.senderName = args[++i];
        break;
      case '--sender-email':
        cmd.senderEmail = args[++i];
        break;
      case '--company':
        cmd.companyName = args[++i];
        break;
      case '--brand-color':
        cmd.brandColor = args[++i];
        break;
      case '--logo':
        cmd.logoUrl = args[++i];
        break;
      case '--output':
        cmd.outputDir = args[++i];
        break;
    }
  }

  return cmd;
}

function getEmailTemplate(type: string, brandColor: string = '#667eea'): any {
  const templates: Record<string, any> = {
    'invoice': {
      subject: 'Invoice {{invoiceNumber}}',
      preheader: 'Invoice for {{clientName}} - Amount: {{amount}}',
      preview: 'Professional invoice email with payment link',
      backgroundColor: '#f5f5f5',
      accentColor: brandColor
    },
    'receipt': {
      subject: 'Receipt {{transactionId}}',
      preheader: 'Thank you for your purchase',
      preview: 'Transaction receipt with download link',
      backgroundColor: '#f5f5f5',
      accentColor: '#27ae60'
    },
    'welcome': {
      subject: 'Welcome to {{companyName}}',
      preheader: 'Get started with your new account',
      preview: 'Onboarding welcome email',
      backgroundColor: '#f5f5f5',
      accentColor: '#3498db'
    },
    'notification': {
      subject: '{{notificationTitle}}',
      preheader: 'You have a new notification',
      preview: 'Alert notification email',
      backgroundColor: '#f5f5f5',
      accentColor: '#f39c12'
    },
    'report': {
      subject: '{{reportTitle}} Report',
      preheader: 'Your {{period}} report is ready',
      preview: 'Business report with metrics',
      backgroundColor: '#f5f5f5',
      accentColor: '#34495e'
    },
    'reminder': {
      subject: 'Reminder: {{reminderTitle}}',
      preheader: 'You have an upcoming {{reminderType}}',
      preview: 'Reminder notification email',
      backgroundColor: '#f5f5f5',
      accentColor: '#e74c3c'
    },
    'payment': {
      subject: 'Payment {{paymentId}} - {{status}}',
      preheader: 'Payment confirmation',
      preview: 'Payment confirmation email',
      backgroundColor: '#f5f5f5',
      accentColor: '#16a085'
    },
    'shipping': {
      subject: 'Your order has shipped',
      preheader: 'Tracking number: {{trackingNumber}}',
      preview: 'Shipping notification email',
      backgroundColor: '#f5f5f5',
      accentColor: '#2980b9'
    },
    'confirmation': {
      subject: 'Confirmation: {{confirmationTitle}}',
      preheader: 'Your {{itemType}} has been confirmed',
      preview: 'Confirmation notification email',
      backgroundColor: '#f5f5f5',
      accentColor: '#27ae60'
    },
    'alert': {
      subject: 'Alert: {{alertTitle}}',
      preheader: 'Urgent: {{alertDescription}}',
      preview: 'Security/system alert email',
      backgroundColor: '#f5f5f5',
      accentColor: '#e74c3c'
    }
  };

  return templates[type] || templates['notification'];
}

function generateHTML(cmd: EmailGenerateCommand, template: any): string {
  const accentColor = cmd.brandColor || template.accentColor;
  const companyName = cmd.companyName || 'Company Name';
  const senderName = cmd.senderName || 'Support Team';
  const senderEmail = cmd.senderEmail || 'support@company.com';

  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>${cmd.subject || template.subject}</title>
  <style>
    body {
      margin: 0;
      padding: 0;
      background: ${template.backgroundColor};
      font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
    }
    .container {
      max-width: 600px;
      margin: 0 auto;
      background: white;
      border-radius: 8px;
      overflow: hidden;
      box-shadow: 0 2px 8px rgba(0,0,0,0.1);
    }
    .header {
      background: linear-gradient(135deg, ${accentColor} 0%, ${this.darkenColor(accentColor)} 100%);
      color: white;
      padding: 40px 20px;
      text-align: center;
    }
    .header h1 {
      margin: 0;
      font-size: 28px;
    }
    .header p {
      margin: 10px 0 0 0;
      opacity: 0.9;
    }
    .content {
      padding: 40px 20px;
      color: #333;
      line-height: 1.6;
    }
    .content h2 {
      color: ${accentColor};
      margin-top: 20px;
    }
    .content p {
      margin: 10px 0;
    }
    .cta-button {
      display: inline-block;
      background: ${accentColor};
      color: white;
      padding: 12px 30px;
      text-decoration: none;
      border-radius: 5px;
      font-weight: bold;
      margin: 20px 0;
      cursor: pointer;
    }
    .cta-button:hover {
      opacity: 0.9;
    }
    .footer {
      background: #f9f9f9;
      border-top: 1px solid #ddd;
      padding: 20px;
      text-align: center;
      color: #666;
      font-size: 12px;
    }
    .divider {
      border: 0;
      height: 1px;
      background: #ddd;
      margin: 20px 0;
    }
    table {
      width: 100%;
      border-collapse: collapse;
      margin: 20px 0;
    }
    th, td {
      padding: 10px;
      text-align: left;
      border-bottom: 1px solid #eee;
    }
    th {
      background: #f9f9f9;
      font-weight: bold;
      color: ${accentColor};
    }
    @media (max-width: 600px) {
      .container {
        border-radius: 0;
      }
      .header {
        padding: 20px;
      }
      .content {
        padding: 20px;
      }
    }
  </style>
</head>
<body>
  <div class="container">
    <!-- Header -->
    <div class="header">
      <h1>{{headerTitle}}</h1>
      <p>{{headerSubtitle}}</p>
    </div>

    <!-- Content -->
    <div class="content">
      <p>Hi {{recipientName}},</p>

      <p>{{mainContent}}</p>

      <!-- Dynamic Section (customize per email type) -->
      {{customSection}}

      <!-- Call to Action -->
      <a href="{{actionLink}}" class="cta-button">{{actionText}}</a>

      <hr class="divider">

      <h2>Questions?</h2>
      <p>If you have any questions, feel free to reach out to us at <strong>${senderEmail}</strong> or call our support team.</p>
    </div>

    <!-- Footer -->
    <div class="footer">
      <p><strong>${companyName}</strong></p>
      <p>${senderName} | ${senderEmail}</p>
      <p>© ${new Date().getFullYear()} ${companyName}. All rights reserved.</p>
    </div>
  </div>
</body>
</html>`;
}

function generatePreviewHTML(cmd: EmailGenerateCommand): string {
  const template = getEmailTemplate(cmd.type, cmd.brandColor);

  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Email Template Preview</title>
  <style>
    body {
      margin: 0;
      padding: 20px;
      background: #f0f0f0;
      font-family: Arial, sans-serif;
    }
    .preview-container {
      max-width: 1200px;
      margin: 0 auto;
    }
    .preview-header {
      background: white;
      padding: 20px;
      border-radius: 5px;
      margin-bottom: 20px;
    }
    .preview-content {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 20px;
      margin-bottom: 20px;
    }
    .preview-box {
      background: white;
      border-radius: 5px;
      padding: 20px;
    }
    .preview-label {
      font-weight: bold;
      margin-bottom: 10px;
      color: #333;
    }
    .preview-value {
      color: #666;
      word-break: break-all;
      background: #f9f9f9;
      padding: 10px;
      border-radius: 3px;
    }
    .iframe-container {
      background: white;
      border-radius: 5px;
      overflow: hidden;
      border: 1px solid #ddd;
    }
    iframe {
      width: 100%;
      height: 600px;
      border: none;
    }
    @media (max-width: 1024px) {
      .preview-content {
        grid-template-columns: 1fr;
      }
    }
  </style>
</head>
<body>
  <div class="preview-container">
    <div class="preview-header">
      <h1>Email Template Preview: ${cmd.type}</h1>
      <p>Email template: <strong>${cmd.name}</strong></p>
    </div>

    <div class="preview-content">
      <div class="preview-box">
        <div class="preview-label">Template Type:</div>
        <div class="preview-value">${cmd.type}</div>

        <div class="preview-label" style="margin-top: 20px;">Subject:</div>
        <div class="preview-value">${template.subject}</div>

        <div class="preview-label" style="margin-top: 20px;">Preheader:</div>
        <div class="preview-value">${template.preheader}</div>

        <div class="preview-label" style="margin-top: 20px;">Preview:</div>
        <div class="preview-value">${template.preview}</div>
      </div>

      <div class="preview-box">
        <div class="preview-label">Sender Name:</div>
        <div class="preview-value">${cmd.senderName || 'Support Team'}</div>

        <div class="preview-label" style="margin-top: 20px;">Sender Email:</div>
        <div class="preview-value">${cmd.senderEmail || 'support@company.com'}</div>

        <div class="preview-label" style="margin-top: 20px;">Company:</div>
        <div class="preview-value">${cmd.companyName || 'Company Name'}</div>

        <div class="preview-label" style="margin-top: 20px;">Brand Color:</div>
        <div class="preview-value">${cmd.brandColor || '#667eea'}</div>
      </div>
    </div>

    <div class="iframe-container">
      <iframe id="preview" srcdoc=""></iframe>
    </div>
  </div>

  <script>
    // Generate preview
    const content = \`${this.generateHTML(cmd, template)}\`;
    document.getElementById('preview').srcdoc = content;
  </script>
</body>
</html>`;
}

function darkenColor(hex: string): string {
  const h = hex.replace('#', '');
  const num = parseInt(h, 16);
  const amt = -20;
  const usePound = num[0] === '#';
  const R = (num >> 16) + amt;
  const G = (num >> 8 & 0x00FF) + amt;
  const B = (num & 0x0000FF) + amt;
  return '#' + (0x1000000 + (R < 255 ? R < 1 ? 0 : R : 255) * 0x10000 +
    (G < 255 ? G < 1 ? 0 : G : 255) * 0x100 + (B < 255 ? B < 1 ? 0 : B : 255))
    .toString(16).slice(1);
}

function ensureDirectory(dir: string): void {
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
}

async function main() {
  try {
    const cmd = parseArgs();
    const template = getEmailTemplate(cmd.type, cmd.brandColor);
    const outputDir = path.resolve(cmd.outputDir || './', cmd.name);

    console.log(`\n📧 Generating ${cmd.type} email template: ${cmd.name}\n`);

    ensureDirectory(outputDir);

    // Generate files
    const html = this.generateHTML(cmd, template);
    const preview = this.generatePreviewHTML(cmd);

    fs.writeFileSync(path.join(outputDir, 'template.html'), html);
    fs.writeFileSync(path.join(outputDir, 'preview.html'), preview);

    // Save metadata
    fs.writeFileSync(
      path.join(outputDir, 'metadata.json'),
      JSON.stringify({
        type: cmd.type,
        name: cmd.name,
        subject: cmd.subject || template.subject,
        sender: {
          name: cmd.senderName || 'Support Team',
          email: cmd.senderEmail || 'support@company.com'
        },
        company: cmd.companyName || 'Company Name',
        brandColor: cmd.brandColor || '#667eea',
        createdAt: new Date().toISOString()
      }, null, 2)
    );

    console.log(`✅ Email template generated successfully!\n`);
    console.log(`📁 Location: ${outputDir}`);
    console.log(`📄 Files: template.html, preview.html, metadata.json`);
    console.log(`\n🔗 Open preview.html to see the email`);
    console.log(`\n📧 Use template.html for sending via email service\n`);

  } catch (error) {
    console.error('❌ Error:', error);
    process.exit(1);
  }
}

main();
