/**
 * Print Manager - Handle printing of forms and pages
 * Provides print preview, styles, and configuration
 */

export interface PrintOptions {
  paperSize?: 'a4' | 'letter' | 'a3';
  orientation?: 'portrait' | 'landscape';
  margins?: {
    top: number;
    right: number;
    bottom: number;
    left: number;
  };
  headers?: boolean;
  footers?: boolean;
  headerText?: string;
  footerText?: string;
  backgroundColor?: boolean;
}

export interface PrintStyle {
  selector: string;
  hide?: boolean;
  pageBreak?: boolean;
  rules: Record<string, string>;
}

export class PrintManager {
  private static readonly DEFAULT_OPTIONS: PrintOptions = {
    paperSize: 'a4',
    orientation: 'portrait',
    margins: { top: 10, right: 10, bottom: 10, left: 10 },
    headers: false,
    footers: false,
    backgroundColor: false
  };

  private static printWindow: Window | null = null;

  /**
   * Open print preview
   */
  static preview(element?: HTMLElement): void {
    if (typeof window === 'undefined') {
      throw new Error('Print preview only works in browser');
    }

    if (element) {
      const printWindow = window.open('', '_blank');
      if (!printWindow) return;

      const html = document.documentElement.innerHTML;
      const styles = this.getDefaultPrintStyles();

      printWindow.document.write(`
<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8">
  <title>Print Preview</title>
  <style>${styles}</style>
</head>
<body>
  ${element.outerHTML}
</body>
</html>
      `);

      printWindow.document.close();
      this.printWindow = printWindow;
    } else {
      window.print();
    }
  }

  /**
   * Print element
   */
  static printElement(
    element: HTMLElement,
    options: Partial<PrintOptions> = {}
  ): void {
    if (typeof window === 'undefined') {
      throw new Error('Print only works in browser');
    }

    const finalOptions = { ...this.DEFAULT_OPTIONS, ...options };
    const styles = this.generatePrintStyles(finalOptions);

    const printWindow = window.open('', '_blank');
    if (!printWindow) return;

    const html = `
<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8">
  <title>Print</title>
  <style>${styles}</style>
</head>
<body>
  ${element.outerHTML}
</body>
</html>
    `;

    printWindow.document.write(html);
    printWindow.document.close();

    setTimeout(() => {
      printWindow.print();
    }, 250);

    this.printWindow = printWindow;
  }

  /**
   * Print form
   */
  static printForm(
    formId: string,
    options: Partial<PrintOptions> = {}
  ): void {
    if (typeof document === 'undefined') {
      throw new Error('Print only works in browser');
    }

    const form = document.getElementById(formId);
    if (!form) {
      throw new Error(`Form with id "${formId}" not found`);
    }

    this.printElement(form as HTMLElement, options);
  }

  /**
   * Generate print styles
   */
  private static generatePrintStyles(options: PrintOptions): string {
    const margins = options.margins || this.DEFAULT_OPTIONS.margins;

    return `
${this.getBasePrintStyles()}

@media print {
  @page {
    size: ${options.paperSize} ${options.orientation};
    margin: ${margins.top}mm ${margins.right}mm ${margins.bottom}mm ${margins.left}mm;
    ${options.headers ? `@top-center { content: "${options.headerText || ''}"; }` : ''}
    ${options.footers ? `@bottom-center { content: "${options.footerText || ''}"; }` : ''}
  }

  body {
    margin: 0;
    padding: 0;
    background: ${options.backgroundColor ? '#fff' : 'transparent'};
    color: #000;
  }

  .no-print, .no-print * {
    display: none !important;
  }

  .page-break {
    page-break-after: always;
  }

  .page-break-before {
    page-break-before: always;
  }

  /* Hide interactive elements */
  button, input[type="button"], input[type="reset"], input[type="submit"],
  input[type="checkbox"], input[type="radio"], select, textarea {
    border: 1px solid #000 !important;
  }

  /* Print-friendly table styles */
  table {
    border-collapse: collapse !important;
    width: 100% !important;
  }

  th, td {
    border: 1px solid #000 !important;
    padding: 8px !important;
    text-align: left;
  }

  th {
    background-color: #f0f0f0 !important;
    font-weight: bold;
  }

  /* Avoid breaking content */
  h1, h2, h3, h4, h5, h6, p, ul, ol, li {
    page-break-inside: avoid !important;
  }

  /* Keep images together with text */
  img {
    max-width: 100%;
    page-break-inside: avoid;
  }

  a {
    color: #000;
    text-decoration: underline;
  }
}
    `;
  }

  /**
   * Get default print styles
   */
  private static getDefaultPrintStyles(): string {
    return this.generatePrintStyles(this.DEFAULT_OPTIONS);
  }

  /**
   * Get base print styles
   */
  private static getBasePrintStyles(): string {
    return `
* {
  -webkit-print-color-adjust: exact;
  print-color-adjust: exact;
}

body {
  font-family: Arial, sans-serif;
  line-height: 1.6;
  color: #333;
}

h1, h2, h3, h4, h5, h6 {
  margin: 1em 0 0.5em 0;
}

p {
  margin: 0.5em 0;
}

table {
  border-collapse: collapse;
  margin: 1em 0;
}

th, td {
  border: 1px solid #999;
  padding: 8px;
}

th {
  background-color: #e0e0e0;
  font-weight: bold;
}

ul, ol {
  margin: 1em 0;
  padding-left: 2em;
}

.container, .form, .page {
  max-width: 100%;
  margin: 0;
  padding: 0;
}

/* Form-specific styles */
.form-group {
  margin-bottom: 1em;
}

.form-label {
  display: block;
  font-weight: bold;
  margin-bottom: 0.5em;
}

input[type="text"],
input[type="email"],
input[type="number"],
input[type="date"],
select,
textarea {
  border: 1px solid #999;
  padding: 0.5em;
  margin-bottom: 0.5em;
}

@media print {
  /* Hide elements with no-print class */
  .no-print {
    display: none !important;
  }

  /* Print button styles */
  button, input[type="button"], input[type="submit"], input[type="reset"] {
    display: none !important;
  }

  /* Show form values instead of inputs */
  input[type="text"],
  input[type="email"],
  input[type="number"],
  input[type="date"],
  select,
  textarea {
    border: none;
    padding: 0.25em 0;
    background: transparent;
  }

  /* Page breaks */
  .page {
    page-break-after: always;
  }

  .page:last-child {
    page-break-after: avoid;
  }

  /* Avoid widows and orphans */
  p {
    page-break-inside: avoid;
  }

  /* Table header on each page */
  thead {
    display: table-header-group;
  }

  tr {
    page-break-inside: avoid;
  }
}
    `;
  }

  /**
   * Add print styles to document
   */
  static addPrintStyles(options: Partial<PrintOptions> = {}): void {
    if (typeof document === 'undefined') return;

    const finalOptions = { ...this.DEFAULT_OPTIONS, ...options };
    const styles = this.generatePrintStyles(finalOptions);

    const styleElement = document.createElement('style');
    styleElement.type = 'text/css';
    styleElement.innerHTML = styles;

    document.head.appendChild(styleElement);
  }

  /**
   * Get printable HTML
   */
  static getPrintableHTML(
    element: HTMLElement,
    options: Partial<PrintOptions> = {}
  ): string {
    const finalOptions = { ...this.DEFAULT_OPTIONS, ...options };
    const styles = this.generatePrintStyles(finalOptions);

    return `
<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Print</title>
  <style>${styles}</style>
</head>
<body>
  ${element.outerHTML}
</body>
</html>
    `;
  }

  /**
   * Close print window
   */
  static closePrintWindow(): void {
    if (this.printWindow && !this.printWindow.closed) {
      this.printWindow.close();
      this.printWindow = null;
    }
  }

  /**
   * Check if print is supported
   */
  static isSupported(): boolean {
    return typeof window !== 'undefined' && typeof window.print === 'function';
  }
}
