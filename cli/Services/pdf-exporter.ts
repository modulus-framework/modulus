/**
 * PDF Exporter - Export forms and pages to PDF
 * Uses html2pdf library for client-side PDF generation
 */

export interface PDFExportOptions {
  filename: string;
  orientation?: 'portrait' | 'landscape';
  pageSize?: 'a4' | 'letter' | 'a3';
  margin?: number;
  compress?: boolean;
  quality?: number;
}

export interface PDFData {
  buffer: ArrayBuffer;
  filename: string;
  mimeType: string;
}

export class PDFExporter {
  private static readonly DEFAULT_OPTIONS: PDFExportOptions = {
    filename: 'export.pdf',
    orientation: 'portrait',
    pageSize: 'a4',
    margin: 10,
    compress: true,
    quality: 2
  };

  /**
   * Export form data to PDF from HTML element
   */
  static async exportFromHTML(
    element: HTMLElement,
    options: Partial<PDFExportOptions> = {}
  ): Promise<PDFData> {
    const finalOptions = { ...this.DEFAULT_OPTIONS, ...options };

    return new Promise((resolve, reject) => {
      try {
        const html2pdf = (window as any).html2pdf;

        if (!html2pdf) {
          throw new Error('html2pdf library not loaded');
        }

        const opt = {
          margin: finalOptions.margin,
          filename: finalOptions.filename,
          image: { type: 'jpeg', quality: finalOptions.quality },
          html2canvas: { scale: 2 },
          jsPDF: {
            orientation: finalOptions.orientation,
            unit: 'mm',
            format: finalOptions.pageSize
          }
        };

        html2pdf()
          .set(opt)
          .from(element)
          .toPdf()
          .output('arraybuffer')
          .then((pdf: ArrayBuffer) => {
            resolve({
              buffer: pdf,
              filename: finalOptions.filename,
              mimeType: 'application/pdf'
            });
          })
          .catch((err: any) => {
            reject(err);
          });
      } catch (error) {
        reject(error);
      }
    });
  }

  /**
   * Export form data as PDF
   */
  static async exportData(
    data: Record<string, any>,
    templateHTML: string,
    options: Partial<PDFExportOptions> = {}
  ): Promise<PDFData> {
    const finalOptions = { ...this.DEFAULT_OPTIONS, ...options };

    return new Promise((resolve, reject) => {
      try {
        const html2pdf = (window as any).html2pdf;

        if (!html2pdf) {
          throw new Error('html2pdf library not loaded');
        }

        const html = this.renderTemplate(templateHTML, data);
        const opt = {
          margin: finalOptions.margin,
          filename: finalOptions.filename,
          image: { type: 'jpeg', quality: finalOptions.quality },
          html2canvas: { scale: 2 },
          jsPDF: {
            orientation: finalOptions.orientation,
            unit: 'mm',
            format: finalOptions.pageSize
          }
        };

        html2pdf()
          .set(opt)
          .from(html)
          .toPdf()
          .output('arraybuffer')
          .then((pdf: ArrayBuffer) => {
            resolve({
              buffer: pdf,
              filename: finalOptions.filename,
              mimeType: 'application/pdf'
            });
          })
          .catch((err: any) => {
            reject(err);
          });
      } catch (error) {
        reject(error);
      }
    });
  }

  /**
   * Export form state to PDF
   */
  static async exportFormState(
    formName: string,
    formData: Record<string, any>,
    options: Partial<PDFExportOptions> = {}
  ): Promise<PDFData> {
    const finalOptions = {
      ...this.DEFAULT_OPTIONS,
      filename: `${formName}.pdf`,
      ...options
    };

    const template = this.getFormTemplate(formName);
    return this.exportData(formData, template, finalOptions);
  }

  /**
   * Export batch of forms to PDF
   */
  static async exportBatch(
    forms: Array<{ name: string; data: Record<string, any> }>,
    options: Partial<PDFExportOptions> = {}
  ): Promise<PDFData[]> {
    const results: PDFData[] = [];

    for (const form of forms) {
      const pdf = await this.exportFormState(form.name, form.data, {
        ...options,
        filename: `${form.name}.pdf`
      });
      results.push(pdf);
    }

    return results;
  }

  /**
   * Download PDF in browser
   */
  static download(pdfData: PDFData): void {
    if (typeof window === 'undefined') {
      throw new Error('Download only works in browser');
    }

    const blob = new Blob([pdfData.buffer], { type: pdfData.mimeType });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');

    link.href = url;
    link.download = pdfData.filename;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  }

  /**
   * Get PDF as data URL
   */
  static async toDataURL(pdfData: PDFData): Promise<string> {
    const blob = new Blob([pdfData.buffer], { type: pdfData.mimeType });
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => {
        resolve(reader.result as string);
      };
      reader.onerror = reject;
      reader.readAsDataURL(blob);
    });
  }

  /**
   * Send PDF to server
   */
  static async sendToServer(
    pdfData: PDFData,
    endpoint: string
  ): Promise<Response> {
    const formData = new FormData();
    const blob = new Blob([pdfData.buffer], { type: pdfData.mimeType });

    formData.append('file', blob, pdfData.filename);

    return fetch(endpoint, {
      method: 'POST',
      body: formData
    });
  }

  /**
   * Render template with data
   */
  private static renderTemplate(template: string, data: Record<string, any>): string {
    let html = template;

    Object.entries(data).forEach(([key, value]) => {
      const regex = new RegExp(`{{${key}}}`, 'g');
      html = html.replace(regex, String(value || ''));
    });

    return html;
  }

  /**
   * Get form template
   */
  private static getFormTemplate(formName: string): string {
    return `
<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8">
  <title>${formName}</title>
  <style>
    body {
      font-family: Arial, sans-serif;
      margin: 0;
      padding: 20px;
      background: white;
    }
    .form-container {
      max-width: 800px;
      margin: 0 auto;
    }
    h1 {
      color: #333;
      border-bottom: 2px solid #667eea;
      padding-bottom: 10px;
    }
    .form-group {
      margin: 20px 0;
      border-bottom: 1px solid #eee;
      padding-bottom: 15px;
    }
    .form-label {
      font-weight: bold;
      color: #333;
      margin-bottom: 5px;
      display: block;
    }
    .form-value {
      color: #666;
      font-size: 14px;
    }
    .timestamp {
      text-align: right;
      color: #999;
      font-size: 12px;
      margin-top: 20px;
      border-top: 1px solid #ddd;
      padding-top: 10px;
    }
  </style>
</head>
<body>
  <div class="form-container">
    <h1>${formName}</h1>
    {{content}}
    <div class="timestamp">
      Generated on {{timestamp}}
    </div>
  </div>
</body>
</html>
    `;
  }
}
