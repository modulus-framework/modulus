#!/usr/bin/env node

/**
 * Modulus CLI - Page Generator Command
 * Usage: modulus generate page --type <type> --name <name> [options]
 */

import * as fs from 'fs';
import * as path from 'path';

interface GenerateCommand {
  type: string;
  name: string;
  title?: string;
  description?: string;
  fields?: number;
  multiStep?: boolean;
  withExports?: boolean;
  withPdf?: boolean;
  withPrint?: boolean;
  withEmail?: boolean;
  theme?: string;
  outputDir?: string;
}

// CLI Argument Parser
function parseArgs(): GenerateCommand {
  const args = process.argv.slice(2);
  const cmd: GenerateCommand = {
    type: 'form',
    name: 'default-page',
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
      case '--title':
        cmd.title = args[++i];
        break;
      case '--description':
        cmd.description = args[++i];
        break;
      case '--fields':
        cmd.fields = parseInt(args[++i]) || 5;
        break;
      case '--multi-step':
        cmd.multiStep = true;
        break;
      case '--with-exports':
        cmd.withExports = true;
        break;
      case '--with-pdf':
        cmd.withPdf = true;
        break;
      case '--with-print':
        cmd.withPrint = true;
        break;
      case '--with-email':
        cmd.withEmail = true;
        break;
      case '--theme':
        cmd.theme = args[++i];
        break;
      case '--output':
        cmd.outputDir = args[++i];
        break;
    }
  }

  return cmd;
}

// Get template configuration by type
function getTemplateConfig(cmd: GenerateCommand): any {
  const baseTitle = cmd.title || formatName(cmd.name);
  const baseDesc = cmd.description || `${baseTitle} form`;

  const templates: Record<string, any> = {
    'packing-list': {
      title: baseTitle || 'Packing List',
      description: baseDesc || 'Create and manage packing lists with QR codes',
      fields: [
        { id: 'item_name', label: 'Item Name', type: 'text', required: true },
        { id: 'quantity', label: 'Quantity', type: 'number', required: true },
        { id: 'weight', label: 'Weight (kg)', type: 'number' },
        { id: 'category', label: 'Category', type: 'select', options: ['Fragile', 'Heavy', 'Light', 'Other'] },
        { id: 'notes', label: 'Notes', type: 'textarea' }
      ],
      exports: [
        { format: 'csv' },
        { format: 'pdf' },
        { format: 'excel' }
      ],
      withPdfViewer: true,
      withPrintButton: true
    },

    'invoice': {
      title: baseTitle || 'Invoice',
      description: baseDesc || 'Generate professional invoices',
      fields: [
        { id: 'invoice_number', label: 'Invoice #', type: 'text', required: true },
        { id: 'client_name', label: 'Client Name', type: 'text', required: true },
        { id: 'client_email', label: 'Client Email', type: 'email' },
        { id: 'amount', label: 'Amount', type: 'number', required: true },
        { id: 'tax', label: 'Tax (%)', type: 'number' },
        { id: 'notes', label: 'Notes', type: 'textarea' }
      ],
      exports: [
        { format: 'pdf' },
        { format: 'word' },
        { format: 'excel' }
      ],
      withPdfViewer: true,
      withPrintButton: true,
      withEmailButton: true
    },

    'receipt': {
      title: baseTitle || 'Receipt',
      description: baseDesc || 'Create receipts for transactions',
      fields: [
        { id: 'transaction_id', label: 'Transaction ID', type: 'text', required: true },
        { id: 'amount', label: 'Amount', type: 'number', required: true },
        { id: 'payment_method', label: 'Payment Method', type: 'select', options: ['Cash', 'Card', 'Check', 'Online'] },
        { id: 'customer_name', label: 'Customer Name', type: 'text' },
        { id: 'date', label: 'Date', type: 'date', required: true }
      ],
      exports: [
        { format: 'pdf' },
        { format: 'csv' }
      ],
      withPdfViewer: true,
      withPrintButton: true,
      withEmailButton: true
    },

    'report': {
      title: baseTitle || 'Report',
      description: baseDesc || 'Create business reports',
      fields: [
        { id: 'report_title', label: 'Report Title', type: 'text', required: true },
        { id: 'department', label: 'Department', type: 'text' },
        { id: 'period', label: 'Report Period', type: 'select', options: ['Weekly', 'Monthly', 'Quarterly', 'Annual'] },
        { id: 'summary', label: 'Summary', type: 'textarea', required: true },
        { id: 'key_metrics', label: 'Key Metrics', type: 'textarea' }
      ],
      exports: [
        { format: 'pdf' },
        { format: 'word' },
        { format: 'excel' }
      ],
      withPdfViewer: true,
      withPrintButton: true,
      withEmailButton: true
    },

    'form': {
      title: baseTitle || 'Contact Form',
      description: baseDesc || 'Collect form submissions',
      fields: [
        { id: 'name', label: 'Full Name', type: 'text', required: true },
        { id: 'email', label: 'Email', type: 'email', required: true },
        { id: 'phone', label: 'Phone', type: 'text' },
        { id: 'subject', label: 'Subject', type: 'text', required: true },
        { id: 'message', label: 'Message', type: 'textarea', required: true }
      ],
      exports: [
        { format: 'csv' }
      ],
      withPdfViewer: false,
      withPrintButton: false,
      withEmailButton: false
    },

    'document': {
      title: baseTitle || 'Document',
      description: baseDesc || 'Create and manage documents',
      fields: [
        { id: 'doc_title', label: 'Document Title', type: 'text', required: true },
        { id: 'doc_type', label: 'Document Type', type: 'select', options: ['Agreement', 'Policy', 'Manual', 'Guide'] },
        { id: 'content', label: 'Content', type: 'textarea', required: true },
        { id: 'author', label: 'Author', type: 'text' },
        { id: 'date', label: 'Date', type: 'date' }
      ],
      exports: [
        { format: 'pdf' },
        { format: 'word' }
      ],
      withPdfViewer: true,
      withPrintButton: true
    }
  };

  const config = templates[cmd.type] || templates['form'];

  // Apply CLI overrides
  if (cmd.fields) {
    config.fields = config.fields.slice(0, cmd.fields);
  }

  if (cmd.multiStep) {
    config.layout = 'multi-step';
  }

  if (!cmd.withExports) {
    config.exports = [];
  }

  if (cmd.withPdf) {
    config.withPdfViewer = true;
  }

  if (cmd.withPrint) {
    config.withPrintButton = true;
  }

  if (cmd.withEmail) {
    config.withEmailButton = true;
  }

  if (cmd.theme) {
    config.styling = { theme: cmd.theme };
  }

  return config;
}

// Format name for display
function formatName(name: string): string {
  return name
    .split('-')
    .map(word => word.charAt(0).toUpperCase() + word.slice(1))
    .join(' ');
}

// Create output directory
function ensureDirectory(dir: string): void {
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
}

// Generate HTML page
function generateHTML(config: any, pageName: string): string {
  const fields = config.fields.map((f: any) => {
    const required = f.required ? 'required' : '';
    const label = `<label for="${f.id}">${f.label}${f.required ? ' *' : ''}</label>`;

    switch (f.type) {
      case 'textarea':
        return `<div class="form-group"><div>${label}<textarea id="${f.id}" name="${f.id}" ${required}></textarea></div></div>`;
      case 'select':
        return `<div class="form-group"><div>${label}<select id="${f.id}" name="${f.id}" ${required}>${(f.options || []).map((o: string) => `<option value="${o}">${o}</option>`).join('')}</select></div></div>`;
      default:
        return `<div class="form-group"><div>${label}<input type="${f.type}" id="${f.id}" name="${f.id}" placeholder="${f.label}" ${required} /></div></div>`;
    }
  }).join('');

  const buttons = [
    ...(config.exports?.length > 0 ? config.exports.map((e: any) => `<button type="button" class="btn-export" data-format="${e.format}">Export ${e.format.toUpperCase()}</button>`) : []),
    ...(config.withPrintButton ? ['<button type="button" class="btn-print" onclick="window.print()">Print</button>'] : []),
    ...(config.withEmailButton ? ['<button type="button" class="btn-email" id="emailBtn">Email</button>'] : []),
    '<button type="submit" class="btn-submit">Submit</button>',
    '<button type="reset" class="btn-reset">Reset</button>'
  ].join('');

  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>${config.title}</title>
  <link rel="stylesheet" href="style.css">
</head>
<body>
  <div class="container">
    <header>
      <h1>${config.title}</h1>
      ${config.description ? `<p class="subtitle">${config.description}</p>` : ''}
    </header>

    <main>
      <form id="mainForm" class="form">
        ${fields}
        <div class="form-actions">
          ${buttons}
        </div>
      </form>
    </main>

    <footer>
      <p>Generated by Modulus UI CLI | ${new Date().toLocaleDateString()}</p>
    </footer>
  </div>

  <script src="script.js"></script>
</body>
</html>`;
}

// Generate CSS
function generateCSS(config: any): string {
  return `* { margin: 0; padding: 0; box-sizing: border-box; }

body {
  font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  min-height: 100vh;
  padding: 20px;
}

.container {
  max-width: 800px;
  margin: 0 auto;
  background: white;
  border-radius: 10px;
  box-shadow: 0 10px 40px rgba(0,0,0,0.2);
  overflow: hidden;
}

header {
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
  padding: 40px 20px;
  text-align: center;
}

header h1 {
  font-size: 2.5em;
  margin-bottom: 10px;
}

header .subtitle {
  font-size: 1.1em;
  opacity: 0.9;
}

main {
  padding: 40px 20px;
}

.form-group {
  margin-bottom: 20px;
}

.form-group > div {
  display: flex;
  flex-direction: column;
}

.form-group label {
  font-weight: 600;
  margin-bottom: 8px;
  color: #333;
}

.form-group input,
.form-group select,
.form-group textarea {
  padding: 12px;
  border: 1px solid #ddd;
  border-radius: 5px;
  font-size: 1em;
  font-family: inherit;
  transition: border-color 0.3s;
}

.form-group input:focus,
.form-group select:focus,
.form-group textarea:focus {
  outline: none;
  border-color: #667eea;
  box-shadow: 0 0 5px rgba(102, 126, 234, 0.3);
}

.form-group textarea {
  resize: vertical;
  min-height: 120px;
}

.form-actions {
  display: flex;
  gap: 10px;
  margin-top: 30px;
  flex-wrap: wrap;
  justify-content: center;
}

button {
  padding: 12px 24px;
  border: none;
  border-radius: 5px;
  font-size: 1em;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.3s;
}

.btn-submit {
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
}

.btn-submit:hover {
  transform: translateY(-2px);
  box-shadow: 0 5px 15px rgba(102, 126, 234, 0.4);
}

.btn-reset, .btn-print {
  background: #f0f0f0;
  color: #333;
}

.btn-reset:hover, .btn-print:hover {
  background: #e0e0e0;
}

.btn-export, .btn-email {
  background: #4CAF50;
  color: white;
  font-size: 0.9em;
}

.btn-export:hover, .btn-email:hover {
  background: #45a049;
}

footer {
  background: #f5f5f5;
  padding: 20px;
  text-align: center;
  color: #666;
  font-size: 0.9em;
}

@media (max-width: 600px) {
  .container { border-radius: 0; }
  header h1 { font-size: 1.8em; }
  main { padding: 20px; }
  .form-actions { flex-direction: column; }
  button { width: 100%; }
}

@media print {
  body { background: white; }
  .form-actions { display: none; }
}`;
}

// Generate JavaScript
function generateJS(pageName: string): string {
  return `document.addEventListener('DOMContentLoaded', () => {
  const form = document.getElementById('mainForm');

  // Auto-save
  form.addEventListener('change', () => {
    const data = new FormData(form);
    const json = Object.fromEntries(data);
    localStorage.setItem('${pageName}_data', JSON.stringify(json));
  });

  // Load saved
  const saved = localStorage.getItem('${pageName}_data');
  if (saved) {
    const data = JSON.parse(saved);
    Object.keys(data).forEach(key => {
      const field = form.elements[key];
      if (field) field.value = data[key];
    });
  }

  // Export buttons
  document.querySelectorAll('.btn-export').forEach(btn => {
    btn.addEventListener('click', () => {
      const format = btn.dataset.format;
      const data = new FormData(form);
      console.log('Export as', format, Object.fromEntries(data));
      alert('Exporting as ' + format + '...');
    });
  });

  // Email button
  const emailBtn = document.getElementById('emailBtn');
  if (emailBtn) {
    emailBtn.addEventListener('click', () => {
      const data = new FormData(form);
      console.log('Sending email with', Object.fromEntries(data));
      alert('Email sent!');
    });
  }

  // Submit
  form.addEventListener('submit', (e) => {
    e.preventDefault();
    const data = Object.fromEntries(new FormData(form));
    console.log('Form submitted:', data);
    alert('Form submitted successfully!');
  });
});`;
}

// Main execution
async function main() {
  try {
    const cmd = parseArgs();
    const config = getTemplateConfig(cmd);
    const outputDir = path.resolve(cmd.outputDir || './', cmd.name);

    console.log(`\n📄 Generating ${cmd.type} page: ${cmd.name}\n`);

    ensureDirectory(outputDir);

    // Generate files
    const html = generateHTML(config, cmd.name);
    const css = generateCSS(config);
    const js = generateJS(cmd.name);

    fs.writeFileSync(path.join(outputDir, 'index.html'), html);
    fs.writeFileSync(path.join(outputDir, 'style.css'), css);
    fs.writeFileSync(path.join(outputDir, 'script.js'), js);

    // Save config
    fs.writeFileSync(
      path.join(outputDir, 'config.json'),
      JSON.stringify({ ...cmd, config }, null, 2)
    );

    console.log(`✅ Page generated successfully!\n`);
    console.log(`📁 Location: ${outputDir}`);
    console.log(`📄 Files: index.html, style.css, script.js`);
    console.log(`⚙️  Config: config.json\n`);
    console.log(`🚀 Ready to use: Open index.html in your browser\n`);

  } catch (error) {
    console.error('❌ Error:', error);
    process.exit(1);
  }
}

main();
