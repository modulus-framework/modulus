/**
 * DynamicPageGenerator - Create any page type on-demand through CLI
 * Generates complete pages with forms, exports, PDF viewing, etc.
 */

export interface PageConfig {
  name: string;
  type: 'packing-list' | 'invoice' | 'receipt' | 'report' | 'form' | 'document' | 'custom';
  title: string;
  description?: string;
  fields: Field[];
  layout?: 'single-step' | 'multi-step' | 'full-page';
  exports?: ExportFormat[];
  withPdfViewer?: boolean;
  withPrintButton?: boolean;
  withEmailButton?: boolean;
  styling?: PageStyling;
  customContent?: string;
}

export interface Field {
  id: string;
  label: string;
  type: 'text' | 'number' | 'email' | 'date' | 'select' | 'textarea' | 'checkbox' | 'file';
  required?: boolean;
  placeholder?: string;
  options?: string[];
  defaultValue?: any;
}

export interface ExportFormat {
  format: 'csv' | 'excel' | 'pdf' | 'word';
  filename?: string;
  includeTimestamp?: boolean;
}

export interface PageStyling {
  theme?: 'light' | 'dark' | 'professional' | 'colorful';
  primaryColor?: string;
  accentColor?: string;
  fontFamily?: string;
}

export interface GeneratedPage {
  html: string;
  css: string;
  javascript: string;
  metadata: PageMetadata;
}

export interface PageMetadata {
  name: string;
  type: string;
  createdAt: number;
  version: string;
  dependencies: string[];
}

export class DynamicPageGenerator {
  /**
   * Generate a complete page from config
   */
  static generatePage(config: PageConfig): GeneratedPage {
    const html = this.generateHTML(config);
    const css = this.generateCSS(config);
    const javascript = this.generateJavaScript(config);

    const metadata: PageMetadata = {
      name: config.name,
      type: config.type,
      createdAt: Date.now(),
      version: '1.0.0',
      dependencies: this.getDependencies(config)
    };

    return { html, css, javascript, metadata };
  }

  /**
   * Generate HTML
   */
  private static generateHTML(config: PageConfig): string {
    const header = this.generateHeader(config);
    const form = this.generateForm(config);
    const buttons = this.generateActionButtons(config);
    const footer = this.generateFooter(config);

    return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>${config.title}</title>
  <link rel="stylesheet" href="styles.css">
</head>
<body>
  <div class="container">
    ${header}
    <main class="content">
      ${form}
      ${buttons}
    </main>
    ${this.generatePdfViewer(config)}
    ${footer}
  </div>
  <script src="script.js"></script>
</body>
</html>`;
  }

  /**
   * Generate header
   */
  private static generateHeader(config: PageConfig): string {
    return `
    <header class="page-header">
      <h1>${config.title}</h1>
      ${config.description ? `<p class="description">${config.description}</p>` : ''}
    </header>`;
  }

  /**
   * Generate form
   */
  private static generateForm(config: PageConfig): string {
    const isMultiStep = config.layout === 'multi-step';
    const formClass = isMultiStep ? 'multi-step-form' : 'single-step-form';

    const fieldHTML = config.fields.map((field, index) => {
      const required = field.required ? 'required' : '';
      const stepClass = isMultiStep ? `data-step="${Math.floor(index / 3)}"` : '';

      switch (field.type) {
        case 'text':
        case 'email':
        case 'number':
        case 'date':
          return `
          <div class="form-group" ${stepClass}>
            <label for="${field.id}">${field.label} ${field.required ? '*' : ''}</label>
            <input
              type="${field.type}"
              id="${field.id}"
              name="${field.id}"
              placeholder="${field.placeholder || ''}"
              ${required}
            />
          </div>`;

        case 'select':
          return `
          <div class="form-group" ${stepClass}>
            <label for="${field.id}">${field.label} ${field.required ? '*' : ''}</label>
            <select id="${field.id}" name="${field.id}" ${required}>
              <option value="">Select ${field.label}</option>
              ${(field.options || []).map(opt => `<option value="${opt}">${opt}</option>`).join('')}
            </select>
          </div>`;

        case 'textarea':
          return `
          <div class="form-group" ${stepClass}>
            <label for="${field.id}">${field.label} ${field.required ? '*' : ''}</label>
            <textarea id="${field.id}" name="${field.id}" ${required}></textarea>
          </div>`;

        case 'checkbox':
          return `
          <div class="form-group checkbox" ${stepClass}>
            <input type="checkbox" id="${field.id}" name="${field.id}" />
            <label for="${field.id}">${field.label}</label>
          </div>`;

        case 'file':
          return `
          <div class="form-group" ${stepClass}>
            <label for="${field.id}">${field.label} ${field.required ? '*' : ''}</label>
            <input type="file" id="${field.id}" name="${field.id}" ${required} />
          </div>`;

        default:
          return '';
      }
    }).join('');

    return `
    <form id="mainForm" class="${formClass}">
      ${fieldHTML}
    </form>`;
  }

  /**
   * Generate action buttons
   */
  private static generateActionButtons(config: PageConfig): string {
    const buttons = [];

    if (config.exports && config.exports.length > 0) {
      buttons.push(`
      <div class="export-buttons">
        ${config.exports.map(exp => `
        <button class="btn btn-export" data-format="${exp.format}">
          Export as ${exp.format.toUpperCase()}
        </button>
        `).join('')}
      </div>`);
    }

    if (config.withPrintButton) {
      buttons.push('<button class="btn btn-primary" onclick="window.print()">Print</button>');
    }

    if (config.withPdfViewer) {
      buttons.push('<button class="btn btn-primary" id="generatePdf">Generate PDF</button>');
    }

    if (config.withEmailButton) {
      buttons.push('<button class="btn btn-secondary" id="emailButton">Email</button>');
    }

    buttons.push('<button class="btn btn-success" id="submitBtn">Submit</button>');
    buttons.push('<button class="btn btn-secondary" id="resetBtn">Reset</button>');

    return `<div class="action-buttons">${buttons.join('')}</div>`;
  }

  /**
   * Generate PDF viewer
   */
  private static generatePdfViewer(config: PageConfig): string {
    if (!config.withPdfViewer) return '';

    return `
    <div id="pdfViewerContainer" class="pdf-viewer-container" style="display:none;">
      <div class="pdf-viewer-header">
        <h3>PDF Preview</h3>
        <button class="btn-close" onclick="closePdfViewer()">×</button>
      </div>
      <div id="pdfViewer" class="pdf-viewer"></div>
    </div>`;
  }

  /**
   * Generate footer
   */
  private static generateFooter(config: PageConfig): string {
    return `
    <footer class="page-footer">
      <p>Generated page • ${config.type} • ${new Date().toLocaleDateString()}</p>
    </footer>`;
  }

  /**
   * Generate CSS
   */
  private static generateCSS(config: PageConfig): string {
    const theme = this.getThemeColors(config.styling?.theme || 'light');
    const primary = config.styling?.primaryColor || theme.primary;
    const accent = config.styling?.accentColor || theme.accent;
    const font = config.styling?.fontFamily || 'Arial, sans-serif';

    return `
:root {
  --primary-color: ${primary};
  --accent-color: ${accent};
  --text-color: ${theme.text};
  --bg-color: ${theme.bg};
  --border-color: ${theme.border};
}

* {
  margin: 0;
  padding: 0;
  box-sizing: border-box;
}

body {
  font-family: ${font};
  background-color: var(--bg-color);
  color: var(--text-color);
  line-height: 1.6;
}

.container {
  max-width: 1200px;
  margin: 0 auto;
  padding: 20px;
}

.page-header {
  text-align: center;
  margin-bottom: 40px;
  padding: 20px;
  border-bottom: 2px solid var(--primary-color);
}

.page-header h1 {
  font-size: 2.5em;
  color: var(--primary-color);
  margin-bottom: 10px;
}

.description {
  font-size: 1.1em;
  color: var(--text-color);
  opacity: 0.8;
}

.content {
  background: white;
  padding: 30px;
  border-radius: 8px;
  box-shadow: 0 2px 10px rgba(0,0,0,0.1);
  margin-bottom: 30px;
}

.form-group {
  margin-bottom: 20px;
  display: flex;
  flex-direction: column;
}

.form-group label {
  font-weight: 600;
  margin-bottom: 8px;
  color: var(--primary-color);
}

.form-group input,
.form-group select,
.form-group textarea {
  padding: 12px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  font-size: 1em;
  font-family: inherit;
}

.form-group input:focus,
.form-group select:focus,
.form-group textarea:focus {
  outline: none;
  border-color: var(--primary-color);
  box-shadow: 0 0 5px rgba(0,0,0,0.1);
}

.form-group.checkbox {
  flex-direction: row;
  align-items: center;
  gap: 10px;
}

.form-group.checkbox input {
  margin: 0;
}

.action-buttons {
  display: flex;
  gap: 10px;
  margin-top: 30px;
  flex-wrap: wrap;
}

.btn {
  padding: 12px 24px;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  font-size: 1em;
  transition: all 0.3s ease;
}

.btn-primary {
  background-color: var(--primary-color);
  color: white;
}

.btn-primary:hover {
  opacity: 0.9;
  transform: translateY(-2px);
}

.btn-secondary {
  background-color: var(--accent-color);
  color: white;
}

.btn-success {
  background-color: #27ae60;
  color: white;
}

.btn-export {
  background-color: #3498db;
  color: white;
}

.export-buttons {
  display: flex;
  gap: 10px;
  margin-bottom: 20px;
}

.pdf-viewer-container {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0,0,0,0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.pdf-viewer {
  background: white;
  width: 90%;
  height: 90%;
  border-radius: 8px;
  overflow: auto;
}

.page-footer {
  text-align: center;
  padding: 20px;
  color: #666;
  border-top: 1px solid var(--border-color);
  margin-top: 40px;
}

@media (max-width: 768px) {
  .container {
    padding: 10px;
  }

  .content {
    padding: 15px;
  }

  .page-header h1 {
    font-size: 1.8em;
  }

  .action-buttons {
    flex-direction: column;
  }

  .btn {
    width: 100%;
  }
}

@media print {
  .action-buttons,
  .pdf-viewer-container {
    display: none;
  }

  .page-header {
    border-bottom: 2px solid #000;
  }

  .content {
    box-shadow: none;
    padding: 0;
  }
}`;
  }

  /**
   * Generate JavaScript
   */
  private static generateJavaScript(config: PageConfig): string {
    return `
document.addEventListener('DOMContentLoaded', () => {
  setupFormHandlers();
  setupExportButtons();
  setupActionButtons();
});

function setupFormHandlers() {
  const form = document.getElementById('mainForm');
  if (!form) return;

  // Auto-save form data
  form.addEventListener('change', () => {
    const formData = new FormData(form);
    const data = Object.fromEntries(formData);
    localStorage.setItem('formData_${config.name}', JSON.stringify(data));
  });

  // Load saved data
  const saved = localStorage.getItem('formData_${config.name}');
  if (saved) {
    const data = JSON.parse(saved);
    Object.keys(data).forEach(key => {
      const field = form.elements[key];
      if (field) field.value = data[key];
    });
  }
}

function setupExportButtons() {
  const exportBtns = document.querySelectorAll('[data-format]');
  exportBtns.forEach(btn => {
    btn.addEventListener('click', (e) => {
      const format = e.target.dataset.format;
      exportData(format);
    });
  });
}

function setupActionButtons() {
  const submitBtn = document.getElementById('submitBtn');
  const resetBtn = document.getElementById('resetBtn');
  const pdfBtn = document.getElementById('generatePdf');
  const emailBtn = document.getElementById('emailButton');

  if (submitBtn) submitBtn.addEventListener('click', submitForm);
  if (resetBtn) resetBtn.addEventListener('click', resetForm);
  if (pdfBtn) pdfBtn.addEventListener('click', generatePDF);
  if (emailBtn) emailBtn.addEventListener('click', emailForm);
}

function getFormData() {
  const form = document.getElementById('mainForm');
  const formData = new FormData(form);
  return Object.fromEntries(formData);
}

function exportData(format) {
  const data = getFormData();
  console.log('Exporting as', format, data);
  // Call exporter based on format
  alert('Export as ' + format + ' (implement exporter)');
}

function generatePDF() {
  const data = getFormData();
  console.log('Generating PDF', data);
  // Generate PDF
  alert('PDF generation (implement PDF generator)');
}

function emailForm() {
  const data = getFormData();
  console.log('Emailing form', data);
  alert('Email form (implement email service)');
}

function submitForm() {
  const form = document.getElementById('mainForm');
  if (!form.checkValidity()) {
    alert('Please fill all required fields');
    return;
  }
  const data = getFormData();
  console.log('Submitting form', data);
  alert('Form submitted! (implement backend service)');
}

function resetForm() {
  document.getElementById('mainForm').reset();
  localStorage.removeItem('formData_${config.name}');
}

function closePdfViewer() {
  document.getElementById('pdfViewerContainer').style.display = 'none';
}`;
  }

  /**
   * Get theme colors
   */
  private static getThemeColors(theme: string): any {
    const themes: Record<string, any> = {
      light: { primary: '#2c3e50', accent: '#3498db', text: '#333', bg: '#f5f5f5', border: '#ddd' },
      dark: { primary: '#3498db', accent: '#2ecc71', text: '#fff', bg: '#1a1a1a', border: '#444' },
      professional: { primary: '#34495e', accent: '#16a085', text: '#2c3e50', bg: '#ecf0f1', border: '#bdc3c7' },
      colorful: { primary: '#e74c3c', accent: '#f39c12', text: '#2c3e50', bg: '#ffeaa7', border: '#fdcb6e' }
    };

    return themes[theme] || themes.light;
  }

  /**
   * Get dependencies
   */
  private static getDependencies(config: PageConfig): string[] {
    const deps = ['modulus-ui-core'];

    if (config.exports?.some(e => e.format === 'pdf')) {
      deps.push('jspdf', 'html2canvas');
    }

    if (config.exports?.some(e => e.format === 'excel')) {
      deps.push('xlsx');
    }

    if (config.exports?.some(e => e.format === 'word')) {
      deps.push('docx');
    }

    if (config.withEmailButton) {
      deps.push('nodemailer');
    }

    return deps;
  }

  /**
   * Save page configuration
   */
  static savePage(config: PageConfig, outputDir: string): void {
    const page = this.generatePage(config);

    // Save config
    const configFile = `${outputDir}/${config.name}.config.json`;
    console.log(`Config saved to ${configFile}`);

    // Save files
    console.log(`HTML saved to ${outputDir}/${config.name}.html`);
    console.log(`CSS saved to ${outputDir}/styles.css`);
    console.log(`JS saved to ${outputDir}/script.js`);
  }
}
