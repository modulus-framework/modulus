/**
 * FormSubmissionHandler - Handles async form submission with server validation
 * Manages form data serialization, submission, and response handling
 */

import { FormValidator } from './form-validator';
import { ValidationError } from './field-validators';

export interface ValidationResponse {
    isValid: boolean;
    data?: any;
    errors: FieldError[];
    warnings: WarningDetail[];
    message?: string;
}

export interface FieldError {
    field: string;
    message: string;
    code?: string;
    severity: 'error' | 'warning' | 'info';
}

export interface WarningDetail {
    field: string;
    message: string;
    code?: string;
}

export interface SubmissionOptions {
    method?: 'POST' | 'PUT' | 'PATCH';
    headers?: Record<string, string>;
    validateOnClient?: boolean;
    timeout?: number;
    retryCount?: number;
}

export class FormSubmissionHandler {
    private formElement: HTMLFormElement;
    private validator?: FormValidator;
    private isSubmitting: boolean = false;
    private lastResponse?: ValidationResponse;

    constructor(
        formElement: HTMLFormElement | string,
        validator?: FormValidator
    ) {
        this.formElement = typeof formElement === 'string'
            ? (document.querySelector(formElement) as HTMLFormElement)
            : formElement;
        this.validator = validator;
    }

    /**
     * Submit form with validation
     */
    async submit(
        endpoint: string,
        options: SubmissionOptions = {}
    ): Promise<ValidationResponse> {
        if (this.isSubmitting) {
            console.warn('Form submission already in progress');
            return {
                isValid: false,
                errors: [{
                    field: 'general',
                    message: 'Form submission already in progress',
                    severity: 'error'
                }],
                warnings: []
            };
        }

        this.isSubmitting = true;

        try {
            // Client-side validation
            if (options.validateOnClient !== false && this.validator) {
                const clientErrors = await this.validator.validateAll();
                if (clientErrors.length > 0) {
                    this.isSubmitting = false;
                    return {
                        isValid: false,
                        errors: clientErrors.map(e => ({
                            field: e.field,
                            message: e.message,
                            code: e.code,
                            severity: 'error'
                        })),
                        warnings: [],
                        message: 'Client-side validation failed'
                    };
                }
            }

            // Server-side validation
            const response = await this.submitToServer(endpoint, options);
            this.lastResponse = response;

            return response;
        } catch (error) {
            this.isSubmitting = false;
            const errorMessage = error instanceof Error ? error.message : 'Form submission failed';
            return {
                isValid: false,
                errors: [{
                    field: 'general',
                    message: errorMessage,
                    code: 'SUBMISSION_ERROR',
                    severity: 'error'
                }],
                warnings: [],
                message: 'An error occurred during form submission'
            };
        } finally {
            this.isSubmitting = false;
        }
    }

    /**
     * Submit form to server
     */
    private async submitToServer(
        endpoint: string,
        options: SubmissionOptions
    ): Promise<ValidationResponse> {
        const method = options.method || 'POST';
        const timeout = options.timeout || 30000;
        const retryCount = options.retryCount || 0;

        let lastError: Error | null = null;

        for (let attempt = 0; attempt <= retryCount; attempt++) {
            try {
                const response = await this.fetchWithTimeout(
                    endpoint,
                    this.createRequestInit(method, options),
                    timeout
                );

                if (!response.ok) {
                    throw new Error(`HTTP ${response.status}: ${response.statusText}`);
                }

                const data = await response.json() as ValidationResponse;
                return data;
            } catch (error) {
                lastError = error instanceof Error ? error : new Error('Unknown error');

                if (attempt < retryCount) {
                    // Wait before retry (exponential backoff)
                    await this.delay(1000 * Math.pow(2, attempt));
                }
            }
        }

        throw lastError || new Error('Form submission failed');
    }

    /**
     * Create fetch request initialization
     */
    private createRequestInit(
        method: string,
        options: SubmissionOptions
    ): RequestInit {
        const headers: Record<string, string> = {
            'X-Requested-With': 'XMLHttpRequest',
            ...options.headers
        };

        return {
            method,
            body: new FormData(this.formElement),
            headers,
            credentials: 'same-origin'
        };
    }

    /**
     * Fetch with timeout
     */
    private fetchWithTimeout(
        url: string,
        init: RequestInit,
        timeout: number
    ): Promise<Response> {
        return Promise.race([
            fetch(url, init),
            new Promise<Response>((_, reject) =>
                setTimeout(() => reject(new Error('Request timeout')), timeout)
            )
        ]);
    }

    /**
     * Delay helper for retries
     */
    private delay(ms: number): Promise<void> {
        return new Promise(resolve => setTimeout(resolve, ms));
    }

    /**
     * Get form data as object
     */
    getFormData(): Record<string, any> {
        const formData = new FormData(this.formElement);
        const data: Record<string, any> = {};

        formData.forEach((value, key) => {
            if (data[key]) {
                if (!Array.isArray(data[key])) {
                    data[key] = [data[key]];
                }
                data[key].push(value);
            } else {
                data[key] = value;
            }
        });

        return data;
    }

    /**
     * Check if last submission was successful
     */
    wasSuccessful(): boolean {
        return this.lastResponse?.isValid ?? false;
    }

    /**
     * Get last validation response
     */
    getLastResponse(): ValidationResponse | undefined {
        return this.lastResponse;
    }

    /**
     * Check if currently submitting
     */
    isSubmittingForm(): boolean {
        return this.isSubmitting;
    }

    /**
     * Get errors for specific field
     */
    getFieldErrors(fieldName: string): FieldError[] {
        if (!this.lastResponse) return [];
        return this.lastResponse.errors.filter(e =>
            e.field.toLowerCase() === fieldName.toLowerCase()
        );
    }

    /**
     * Get all error messages
     */
    getAllErrorMessages(): string[] {
        if (!this.lastResponse) return [];
        return this.lastResponse.errors.map(e => e.message);
    }

    /**
     * Check if has errors
     */
    hasErrors(): boolean {
        return this.lastResponse?.isValid === false;
    }
}

// Export for global use
if (typeof window !== 'undefined') {
    (window as any).FormSubmissionHandler = FormSubmissionHandler;
}
