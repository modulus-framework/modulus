/**
 * Document Generator - Generate Word (.docx) documents
 * Creates professional Word documents from templates and data
 */

export interface DocumentSection {
  type: 'heading' | 'paragraph' | 'table' | 'list' | 'image';
  content?: string;
  level?: number;
  rows?: Array<Record<string, string>>;
  columns?: string[];
  items?: string[];
  style?: DocumentStyle;
}

export interface DocumentStyle {
  fontSize?: number;
  bold?: boolean;
  italic?: boolean;
  color?: string;
  alignment?: 'left' | 'center' | 'right' | 'justify';
  spacing?: number;
}

export interface DocumentConfig {
  title: string;
  author?: string;
  company?: string;
  subject?: string;
  sections: DocumentSection[];
  headerText?: string;
  footerText?: string;
  pageBreaks?: boolean;
}

export interface DocumentData {
  content: Uint8Array;
  filename: string;
  mimeType: string;
}

export class DocumentGenerator {
  private static readonly MIME_TYPE = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';

  /**
   * Generate Word document from config
   */
  static async generate(config: DocumentConfig): Promise<DocumentData> {
    return new Promise(async (resolve, reject) => {
      try {
        const { Document, Packer, Paragraph, Table, TableRow, TableCell, AlignmentType, BorderStyle } = await this.loadDocxLibrary();

        const sections = config.sections.map((section, idx) => {
          const pageBreak = config.pageBreaks && idx > 0;
          return this.buildSection(section, Document, Paragraph, Table, TableRow, TableCell, pageBreak);
        });

        const doc = new Document({
          sections: [{
            properties: {},
            children: [
              new Paragraph({
                text: config.title,
                style: 'Heading1',
                spacing: { line: 360, after: 240 }
              }),
              ...(config.subject ? [
                new Paragraph({
                  text: config.subject,
                  style: 'Heading2',
                  spacing: { after: 240 }
                })
              ] : []),
              ...sections.flat(),
              new Paragraph({ text: '' }),
              new Paragraph({
                text: `Generated on ${new Date().toLocaleDateString()}`,
                spacing: { before: 240 },
                style: 'Footer'
              })
            ]
          }]
        });

        const buffer = await Packer.toBuffer(doc);
        resolve({
          content: new Uint8Array(buffer),
          filename: `${config.title.replace(/\s+/g, '_')}.docx`,
          mimeType: this.MIME_TYPE
        });
      } catch (error) {
        reject(error);
      }
    });
  }

  /**
   * Generate document from template with data
   */
  static async generateFromTemplate(
    template: DocumentConfig,
    data: Record<string, any>
  ): Promise<DocumentData> {
    const filled = this.fillTemplate(template, data);
    return this.generate(filled);
  }

  /**
   * Generate invoice document
   */
  static async generateInvoice(data: {
    invoiceNumber: string;
    date: string;
    clientName: string;
    clientEmail: string;
    items: Array<{ description: string; quantity: number; rate: number }>;
    total: number;
    tax?: number;
    notes?: string;
  }): Promise<DocumentData> {
    const template: DocumentConfig = {
      title: `Invoice ${data.invoiceNumber}`,
      subject: `Invoice for ${data.clientName}`,
      sections: [
        {
          type: 'heading',
          content: 'Invoice Details',
          level: 1
        },
        {
          type: 'paragraph',
          content: `Date: ${data.date}\nClient: ${data.clientName}\nEmail: ${data.clientEmail}`
        },
        {
          type: 'heading',
          content: 'Items',
          level: 2
        },
        {
          type: 'table',
          columns: ['Description', 'Quantity', 'Rate', 'Amount'],
          rows: data.items.map(item => ({
            Description: item.description,
            Quantity: item.quantity.toString(),
            Rate: `$${item.rate.toFixed(2)}`,
            Amount: `$${(item.quantity * item.rate).toFixed(2)}`
          }))
        },
        {
          type: 'paragraph',
          content: `Subtotal: $${data.total.toFixed(2)}`
        },
        ...(data.tax ? [
          {
            type: 'paragraph',
            content: `Tax (${data.tax}%): $${(data.total * data.tax / 100).toFixed(2)}`
          }
        ] : []),
        {
          type: 'paragraph',
          content: `Total: $${(data.total * (1 + (data.tax || 0) / 100)).toFixed(2)}`,
          style: { bold: true, fontSize: 14 }
        },
        ...(data.notes ? [
          {
            type: 'paragraph',
            content: `Notes: ${data.notes}`
          }
        ] : [])
      ]
    };

    return this.generate(template);
  }

  /**
   * Generate report document
   */
  static async generateReport(data: {
    title: string;
    period: string;
    sections: Array<{
      heading: string;
      content: string;
      metrics?: Record<string, string>;
    }>;
  }): Promise<DocumentData> {
    const sections: DocumentSection[] = [
      {
        type: 'paragraph',
        content: `Report Period: ${data.period}`,
        style: { italic: true }
      }
    ];

    data.sections.forEach(section => {
      sections.push({
        type: 'heading',
        content: section.heading,
        level: 2
      });
      sections.push({
        type: 'paragraph',
        content: section.content
      });

      if (section.metrics) {
        sections.push({
          type: 'table',
          columns: ['Metric', 'Value'],
          rows: Object.entries(section.metrics).map(([key, value]) => ({
            Metric: key,
            Value: value
          }))
        });
      }
    });

    const template: DocumentConfig = {
      title: data.title,
      subject: `Report - ${data.period}`,
      sections,
      pageBreaks: true
    };

    return this.generate(template);
  }

  /**
   * Export document as binary
   */
  static toBuffer(doc: DocumentData): Buffer {
    if (typeof Buffer !== 'undefined') {
      return Buffer.from(doc.content);
    }
    throw new Error('Node.js Buffer not available');
  }

  /**
   * Send document to server
   */
  static async sendToServer(
    doc: DocumentData,
    endpoint: string
  ): Promise<Response> {
    const blob = new Blob([doc.content], { type: doc.mimeType });
    const formData = new FormData();

    formData.append('file', blob, doc.filename);

    return fetch(endpoint, {
      method: 'POST',
      body: formData
    });
  }

  /**
   * Download document in browser
   */
  static download(doc: DocumentData): void {
    if (typeof window === 'undefined') {
      throw new Error('Download only works in browser');
    }

    const blob = new Blob([doc.content], { type: doc.mimeType });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');

    link.href = url;
    link.download = doc.filename;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  }

  /**
   * Build document section
   */
  private static buildSection(
    section: DocumentSection,
    Document: any,
    Paragraph: any,
    Table: any,
    TableRow: any,
    TableCell: any,
    pageBreak: boolean
  ): any[] {
    const result: any[] = [];

    if (pageBreak) {
      result.push(new Paragraph({ pageBreakBefore: true }));
    }

    switch (section.type) {
      case 'heading':
        result.push(new Paragraph({
          text: section.content,
          style: `Heading${section.level || 1}`,
          spacing: { after: 200 }
        }));
        break;

      case 'paragraph':
        result.push(new Paragraph({
          text: section.content,
          spacing: { after: 200 },
          ...(section.style && {
            bold: section.style.bold,
            italic: section.style.italic,
            size: section.style.fontSize ? section.style.fontSize * 2 : undefined
          })
        }));
        break;

      case 'table':
        if (section.rows && section.columns) {
          const headerRow = new TableRow({
            children: section.columns.map(col =>
              new TableCell({
                children: [new Paragraph({ text: col, bold: true })]
              })
            )
          });

          const bodyRows = section.rows.map(row =>
            new TableRow({
              children: section.columns!.map(col =>
                new TableCell({
                  children: [new Paragraph({ text: row[col] || '' })]
                })
              )
            })
          );

          result.push(new Table({
            rows: [headerRow, ...bodyRows],
            width: { size: 100, type: 'pct' }
          }));
        }
        break;

      case 'list':
        if (section.items) {
          section.items.forEach(item => {
            result.push(new Paragraph({
              text: item,
              bullet: { level: 0 },
              spacing: { after: 100 }
            }));
          });
        }
        break;
    }

    return result;
  }

  /**
   * Fill template with data
   */
  private static fillTemplate(template: DocumentConfig, data: Record<string, any>): DocumentConfig {
    return {
      ...template,
      title: this.interpolate(template.title, data),
      subject: template.subject ? this.interpolate(template.subject, data) : undefined,
      sections: template.sections.map(section => ({
        ...section,
        content: section.content ? this.interpolate(section.content, data) : undefined
      }))
    };
  }

  /**
   * Interpolate variables in string
   */
  private static interpolate(str: string, data: Record<string, any>): string {
    return str.replace(/{{(\w+)}}/g, (match, key) => String(data[key] || match));
  }

  /**
   * Load docx library (lazy load)
   */
  private static async loadDocxLibrary(): Promise<any> {
    try {
      const docx = require('docx');
      return docx;
    } catch {
      throw new Error('docx library not installed. Install with: npm install docx');
    }
  }
}
