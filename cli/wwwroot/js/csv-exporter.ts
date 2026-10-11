/**
 * CSVExporter - Export form data to CSV format
 * Handles data formatting and CSV generation
 */

export interface CSVOptions {
  includeHeaders?: boolean;
  delimiter?: string;
  includeTimestamp?: boolean;
  dateFormat?: string;
  escapeQuotes?: boolean;
}

export interface CSVData {
  filename: string;
  content: string;
  mimeType: string;
}

export class CSVExporter {
  /**
   * Export data to CSV
   */
  static exportData(data: any[], options: CSVOptions = {}): CSVData {
    const {
      includeHeaders = true,
      delimiter = ',',
      includeTimestamp = true,
      dateFormat = 'ISO',
      escapeQuotes = true
    } = options;

    if (!Array.isArray(data) || data.length === 0) {
      throw new Error('Data must be a non-empty array');
    }

    const lines: string[] = [];

    // Add headers if requested
    if (includeHeaders) {
      const headers = this.getHeaders(data[0]);
      lines.push(this.formatRow(headers, delimiter, escapeQuotes));
    }

    // Add data rows
    for (const row of data) {
      const values = this.getValues(row, dateFormat);
      lines.push(this.formatRow(values, delimiter, escapeQuotes));
    }

    // Add timestamp if requested
    let content = lines.join('\n');
    if (includeTimestamp) {
      content = `# Exported: ${new Date().toISOString()}\n${content}`;
    }

    const timestamp = new Date().toISOString().slice(0, 10);
    const filename = `export_${timestamp}.csv`;

    return {
      filename,
      content,
      mimeType: 'text/csv'
    };
  }

  /**
   * Export form state to CSV
   */
  static exportFormState(formId: string, formData: any, options: CSVOptions = {}): CSVData {
    const flatData = this.flattenObject(formData);

    const data = [{
      formId,
      timestamp: new Date().toISOString(),
      ...flatData
    }];

    return this.exportData(data, options);
  }

  /**
   * Export multiple forms to CSV
   */
  static exportBatch(forms: Array<{ formId: string; data: any }>, options: CSVOptions = {}): CSVData {
    const data = forms.map(form => ({
      formId: form.formId,
      timestamp: new Date().toISOString(),
      ...this.flattenObject(form.data)
    }));

    return this.exportData(data, options);
  }

  /**
   * Get headers from object
   */
  private static getHeaders(obj: any): string[] {
    return Object.keys(obj).sort();
  }

  /**
   * Get values from object
   */
  private static getValues(obj: any, dateFormat: string): any[] {
    return this.getHeaders(obj).map(key => {
      const value = obj[key];

      if (value instanceof Date) {
        return dateFormat === 'ISO' ? value.toISOString() : value.toLocaleDateString();
      }

      if (value === null || value === undefined) {
        return '';
      }

      if (typeof value === 'object') {
        return JSON.stringify(value);
      }

      return value;
    });
  }

  /**
   * Format row for CSV
   */
  private static formatRow(values: any[], delimiter: string, escapeQuotes: boolean): string {
    return values.map(value => {
      let str = String(value);

      if (escapeQuotes && str.includes('"')) {
        str = str.replace(/"/g, '""');
      }

      if (str.includes(delimiter) || str.includes('"') || str.includes('\n')) {
        str = `"${str}"`;
      }

      return str;
    }).join(delimiter);
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
   * Download CSV file
   */
  static download(csvData: CSVData): void {
    const blob = new Blob([csvData.content], { type: csvData.mimeType });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');

    link.href = url;
    link.download = csvData.filename;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  }

  /**
   * Get CSV as string
   */
  static toString(csvData: CSVData): string {
    return csvData.content;
  }
}
