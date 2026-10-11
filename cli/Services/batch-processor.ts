/**
 * Batch Processor - Process multiple files/forms in parallel
 * Handles batch export, page generation, email sending, etc.
 */

import * as fs from 'fs';
import * as path from 'path';

export interface BatchItem {
  id: string;
  input: string | Record<string, any>;
  output?: string;
  status?: 'pending' | 'processing' | 'completed' | 'failed';
  error?: string;
  result?: any;
}

export interface BatchOptions {
  parallel?: number;
  format?: 'csv' | 'excel' | 'pdf' | 'word' | 'all';
  inputDir?: string;
  outputDir?: string;
  template?: string;
  onProgress?: (completed: number, total: number, current: string) => void;
  continueOnError?: boolean;
}

export interface BatchResult {
  total: number;
  completed: number;
  failed: number;
  duration: number;
  items: BatchItem[];
  errors: Array<{
    id: string;
    error: string;
  }>;
}

export class BatchProcessor {
  private static readonly DEFAULT_OPTIONS: BatchOptions = {
    parallel: 4,
    format: 'all',
    continueOnError: true
  };

  /**
   * Process batch of files
   */
  static async processBatch(
    files: string[],
    processor: (file: string) => Promise<any>,
    options: Partial<BatchOptions> = {}
  ): Promise<BatchResult> {
    const finalOptions = { ...this.DEFAULT_OPTIONS, ...options };
    const startTime = Date.now();
    const items: BatchItem[] = [];
    const errors: Array<{ id: string; error: string }> = [];
    let completed = 0;

    const createItem = (file: string): BatchItem => ({
      id: path.basename(file),
      input: file,
      status: 'pending'
    });

    const processItem = async (item: BatchItem): Promise<void> => {
      try {
        item.status = 'processing';
        finalOptions.onProgress?.(completed, files.length, item.id);

        item.result = await processor(item.input as string);
        item.status = 'completed';
      } catch (error) {
        item.status = 'failed';
        item.error = String(error);

        if (!finalOptions.continueOnError) {
          throw error;
        }

        errors.push({
          id: item.id,
          error: item.error
        });
      } finally {
        completed++;
        finalOptions.onProgress?.(completed, files.length, item.id);
      }
    };

    const batchItems = files.map(createItem);
    const parallelCount = finalOptions.parallel || 4;

    for (let i = 0; i < batchItems.length; i += parallelCount) {
      const batch = batchItems.slice(i, i + parallelCount);
      await Promise.all(batch.map(processItem));
    }

    const duration = Date.now() - startTime;

    return {
      total: files.length,
      completed,
      failed: errors.length,
      duration,
      items: batchItems,
      errors
    };
  }

  /**
   * Process batch export
   */
  static async processBatchExport(
    inputDir: string,
    outputDir: string,
    format: string = 'csv',
    options: Partial<BatchOptions> = {}
  ): Promise<BatchResult> {
    if (!fs.existsSync(inputDir)) {
      throw new Error(`Input directory not found: ${inputDir}`);
    }

    if (!fs.existsSync(outputDir)) {
      fs.mkdirSync(outputDir, { recursive: true });
    }

    const files = fs.readdirSync(inputDir)
      .filter(f => f.endsWith('.json'))
      .map(f => path.join(inputDir, f));

    const exporter = async (file: string): Promise<any> => {
      const data = JSON.parse(fs.readFileSync(file, 'utf-8'));
      const baseName = path.basename(file, '.json');
      const outputFile = path.join(outputDir, `${baseName}.${format}`);

      return {
        input: file,
        output: outputFile,
        format,
        dataSize: JSON.stringify(data).length
      };
    };

    return this.processBatch(files, exporter, {
      ...options,
      inputDir,
      outputDir,
      format: format as any
    });
  }

  /**
   * Process batch pages
   */
  static async processBatchPages(
    config: Array<{
      type: string;
      name: string;
      [key: string]: any;
    }>,
    pageGenerator: (cfg: any) => Promise<any>,
    options: Partial<BatchOptions> = {}
  ): Promise<BatchResult> {
    const generator = async (configStr: string): Promise<any> => {
      const cfg = JSON.parse(configStr);
      return pageGenerator(cfg);
    };

    return this.processBatch(
      config.map(c => JSON.stringify(c)),
      generator,
      options
    );
  }

  /**
   * Process batch emails
   */
  static async processBatchEmails(
    recipients: Array<{
      email: string;
      name: string;
      variables?: Record<string, any>;
    }>,
    emailSender: (email: string, variables?: any) => Promise<any>,
    options: Partial<BatchOptions> = {}
  ): Promise<BatchResult> {
    const sender = async (recipientStr: string): Promise<any> => {
      const recipient = JSON.parse(recipientStr);
      return emailSender(recipient.email, recipient.variables);
    };

    return this.processBatch(
      recipients.map(r => JSON.stringify(r)),
      sender,
      options
    );
  }

  /**
   * Process with retry logic
   */
  static async processWithRetry<T>(
    items: T[],
    processor: (item: T) => Promise<any>,
    options: Partial<BatchOptions> & { maxRetries?: number } = {}
  ): Promise<BatchResult> {
    const maxRetries = options.maxRetries || 3;
    const finalOptions = { ...this.DEFAULT_OPTIONS, ...options };
    const startTime = Date.now();
    const results: BatchItem[] = [];
    const errors: Array<{ id: string; error: string }> = [];
    let completed = 0;

    const processWithRetryHelper = async (item: T, index: number): Promise<BatchItem> => {
      const result: BatchItem = {
        id: String(index),
        input: item,
        status: 'pending'
      };

      for (let attempt = 1; attempt <= maxRetries; attempt++) {
        try {
          result.status = 'processing';
          finalOptions.onProgress?.(completed, items.length, `Item ${index} (attempt ${attempt})`);

          result.result = await processor(item);
          result.status = 'completed';
          break;
        } catch (error) {
          result.error = String(error);

          if (attempt === maxRetries) {
            result.status = 'failed';
            errors.push({
              id: result.id,
              error: result.error
            });

            if (!finalOptions.continueOnError) {
              throw error;
            }
          }
        }
      }

      completed++;
      finalOptions.onProgress?.(completed, items.length, `Item ${index}`);
      return result;
    };

    const parallelCount = finalOptions.parallel || 4;

    for (let i = 0; i < items.length; i += parallelCount) {
      const batch = items.slice(i, i + parallelCount);
      const batchResults = await Promise.all(
        batch.map((item, idx) => processWithRetryHelper(item, i + idx))
      );
      results.push(...batchResults);
    }

    const duration = Date.now() - startTime;

    return {
      total: items.length,
      completed,
      failed: errors.length,
      duration,
      items: results,
      errors
    };
  }

  /**
   * Generate progress report
   */
  static generateReport(result: BatchResult): string {
    const successRate = ((result.completed / result.total) * 100).toFixed(2);
    const avgTime = (result.duration / result.total).toFixed(2);

    return `
╔════════════════════════════════════════╗
║      Batch Processing Report            ║
╚════════════════════════════════════════╝

Total Items:    ${result.total}
Completed:      ${result.completed}
Failed:         ${result.failed}
Success Rate:   ${successRate}%

Duration:       ${result.duration}ms
Avg per item:   ${avgTime}ms

${result.errors.length > 0 ? `
Errors:
${result.errors.map(e => `  • ${e.id}: ${e.error}`).join('\n')}
` : ''}
    `;
  }

  /**
   * Export results to file
   */
  static exportResults(
    result: BatchResult,
    outputFile: string,
    format: 'json' | 'csv' = 'json'
  ): void {
    if (format === 'json') {
      fs.writeFileSync(outputFile, JSON.stringify(result, null, 2));
    } else {
      const csv = this.convertResultsToCSV(result);
      fs.writeFileSync(outputFile, csv);
    }
  }

  /**
   * Convert results to CSV
   */
  private static convertResultsToCSV(result: BatchResult): string {
    const headers = ['ID', 'Status', 'Input', 'Output', 'Error'];
    const rows = result.items.map(item => [
      item.id,
      item.status,
      String(item.input),
      item.output || '',
      item.error || ''
    ]);

    return [
      headers.join(','),
      ...rows.map(r => r.map(c => `"${c}"`).join(','))
    ].join('\n');
  }

  /**
   * Cancel batch processing
   */
  static async cancel(): Promise<void> {
    return Promise.resolve();
  }

  /**
   * Get processing status
   */
  static getStatus(result: BatchResult): {
    percentage: number;
    estimated_remaining: number;
    status: string;
  } {
    const percentage = Math.round((result.completed / result.total) * 100);
    const avgTimePerItem = result.duration / result.completed;
    const remainingItems = result.total - result.completed;
    const estimatedRemaining = Math.round(avgTimePerItem * remainingItems);

    return {
      percentage,
      estimated_remaining: estimatedRemaining,
      status: `${result.completed}/${result.total} completed`
    };
  }
}
