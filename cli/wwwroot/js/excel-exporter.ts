/**
 * ExcelExporter - Export form data to Excel (XLSX) format
 * Creates professional Excel workbooks with formatting
 */

export interface ExcelOptions {
  includeHeaders?: boolean;
  sheetName?: string;
  autoWidth?: boolean;
  headerStyle?: ExcelStyle;
  dataStyle?: ExcelStyle;
  freezeHeader?: boolean;
}

export interface ExcelStyle {
  backgroundColor?: string;
  fontColor?: string;
  fontBold?: boolean;
  fontSize?: number;
  border?: boolean;
}

export interface ExcelData {
  filename: string;
  buffer: ArrayBuffer;
  mimeType: string;
}

export class ExcelExporter {
  private static readonly MIME_TYPE = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

  /**
   * Export data to Excel
   */
  static exportData(data: any[], options: ExcelOptions = {}): ExcelData {
    const {
      includeHeaders = true,
      sheetName = 'Data',
      autoWidth = true,
      headerStyle = { fontBold: true, backgroundColor: '#4472C4', fontColor: '#FFFFFF' },
      freezeHeader = true
    } = options;

    if (!Array.isArray(data) || data.length === 0) {
      throw new Error('Data must be a non-empty array');
    }

    // Create workbook structure
    const worksheet = this.createWorksheet(data, sheetName, includeHeaders, autoWidth, headerStyle, freezeHeader);
    const workbook = this.createWorkbook(worksheet, sheetName);

    // Generate XLSX
    const buffer = this.generateXLSX(workbook);

    const timestamp = new Date().toISOString().slice(0, 10);
    const filename = `export_${timestamp}.xlsx`;

    return {
      filename,
      buffer,
      mimeType: this.MIME_TYPE
    };
  }

  /**
   * Export form state to Excel
   */
  static exportFormState(formId: string, formData: any, options: ExcelOptions = {}): ExcelData {
    const flatData = this.flattenObject(formData);
    const data = [{
      formId,
      timestamp: new Date().toISOString(),
      ...flatData
    }];

    return this.exportData(data, { ...options, sheetName: formId });
  }

  /**
   * Export batch to Excel with multiple sheets
   */
  static exportBatch(forms: Array<{ formId: string; data: any }>, options: ExcelOptions = {}): ExcelData {
    const workbook = this.createEmptyWorkbook();

    for (const form of forms) {
      const flatData = this.flattenObject(form.data);
      const data = [{
        timestamp: new Date().toISOString(),
        ...flatData
      }];

      const worksheet = this.createWorksheet(
        data,
        form.formId.slice(0, 31), // Excel sheet name limit
        true,
        true,
        options.headerStyle,
        true
      );

      workbook.worksheets.push(worksheet);
    }

    const buffer = this.generateXLSX(workbook);
    const timestamp = new Date().toISOString().slice(0, 10);
    const filename = `batch_export_${timestamp}.xlsx`;

    return { filename, buffer, mimeType: this.MIME_TYPE };
  }

  /**
   * Create worksheet
   */
  private static createWorksheet(
    data: any[],
    name: string,
    includeHeaders: boolean,
    autoWidth: boolean,
    headerStyle: ExcelStyle,
    freezeHeader: boolean
  ): any {
    const headers = this.getHeaders(data[0]);
    const rows: any[] = [];

    // Add headers
    if (includeHeaders) {
      rows.push(headers.map(h => ({ value: h, style: headerStyle })));
    }

    // Add data rows
    for (const row of data) {
      const values = headers.map(h => this.getValue(row, h));
      rows.push(values.map(v => ({ value: v })));
    }

    // Calculate column widths
    const columnWidths = autoWidth ? this.calculateColumnWidths(headers, data) : undefined;

    return {
      name: name.slice(0, 31),
      rows,
      columnWidths,
      freezePane: freezeHeader ? { row: 1, column: 0 } : undefined
    };
  }

  /**
   * Create workbook
   */
  private static createWorkbook(worksheet: any, sheetName: string): any {
    return {
      worksheets: [worksheet],
      metadata: {
        creator: 'Modulus UI',
        created: new Date()
      }
    };
  }

  /**
   * Create empty workbook
   */
  private static createEmptyWorkbook(): any {
    return {
      worksheets: [],
      metadata: {
        creator: 'Modulus UI',
        created: new Date()
      }
    };
  }

  /**
   * Generate XLSX buffer (simplified - would use xlsx library in production)
   */
  private static generateXLSX(workbook: any): ArrayBuffer {
    // This is a placeholder. In production, use xlsx library:
    // import XLSX from 'xlsx';
    // return XLSX.write(workbook, { bookType: 'xlsx', type: 'array' });

    const data = JSON.stringify(workbook);
    const buffer = new ArrayBuffer(data.length);
    const view = new Uint8Array(buffer);

    for (let i = 0; i < data.length; i++) {
      view[i] = data.charCodeAt(i);
    }

    return buffer;
  }

  /**
   * Get headers from object
   */
  private static getHeaders(obj: any): string[] {
    return Object.keys(obj).sort();
  }

  /**
   * Get value from object
   */
  private static getValue(obj: any, key: string): any {
    const value = this.getNestedValue(obj, key);

    if (value instanceof Date) {
      return value.toISOString();
    }

    if (value === null || value === undefined) {
      return '';
    }

    if (typeof value === 'object') {
      return JSON.stringify(value);
    }

    return value;
  }

  /**
   * Get nested value
   */
  private static getNestedValue(obj: any, path: string): any {
    return path.split('_').reduce((current, key) => current?.[key], obj);
  }

  /**
   * Calculate column widths
   */
  private static calculateColumnWidths(headers: string[], data: any[]): number[] {
    return headers.map(header => {
      const maxLength = Math.max(
        header.length,
        ...data.map(row => String(this.getValue(row, header)).length)
      );

      return Math.min(maxLength + 2, 50); // Max width 50
    });
  }

  /**
   * Flatten nested object
   */
  private static flattenObject(obj: any, prefix: string = ''): any {
    const flattened: any = {};

    for (const [key, value] of Object.entries(obj)) {
      const newKey = prefix ? `${prefix}_${key}` : key;

      if (value !== null && typeof value === 'object' && !Array.isArray(value) && !(value instanceof Date)) {
        Object.assign(flattened, this.flattenObject(value, newKey));
      } else {
        flattened[newKey] = value;
      }
    }

    return flattened;
  }

  /**
   * Download Excel file
   */
  static download(excelData: ExcelData): void {
    const blob = new Blob([excelData.buffer], { type: excelData.mimeType });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');

    link.href = url;
    link.download = excelData.filename;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  }
}
