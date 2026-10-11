/**
 * AsyncValidator - Real-time async validation from server
 * Validates fields against server-side rules (username availability, email uniqueness, etc.)
 */

import { ValidationError } from './field-validators';

export interface AsyncValidationRule {
    endpoint: string;
    debounceMs?: number;
    onSuccess?: (field: string) => void;
    onError?: (field: string, error: ValidationError) => void;
}

export class AsyncValidator {
    private fieldName: string;
    private endpoint: string;
    private debounceTimer: number | null = null;
    private debounceMs: number;
    private lastValidatedValue: string = '';
    private isValidating: boolean = false;
    private lastError: ValidationError | null = null;
    private onSuccess?: (field: string) => void;
    private onError?: (field: string, error: ValidationError) => void;

    constructor(
        fieldName: string,
        endpoint?: string,
        debounceMs: number = 500
    ) {
        this.fieldName = fieldName;
        this.endpoint = endpoint || `/api/validate/${fieldName}`;
        this.debounceMs = debounceMs;
    }

    /**
     * Set validation endpoint
     */
    setEndpoint(endpoint: string): AsyncValidator {
        this.endpoint = endpoint;
        return this;
    }

    /**
     * Set debounce delay
     */
    setDebounceMs(ms: number): AsyncValidator {
        this.debounceMs = ms;
        return this;
    }

    /**
     * Set success callback
     */
    onSuccessCallback(callback: (field: string) => void): AsyncValidator {
        this.onSuccess = callback;
        return this;
    }

    /**
     * Set error callback
     */
    onErrorCallback(callback: (field: string, error: ValidationError) => void): AsyncValidator {
        this.onError = callback;
        return this;
    }

    /**
     * Validate value asynchronously with debouncing
     */
    async validate(value: string, immediate: boolean = false): Promise<ValidationError | null> {
        // Clear previous timer
        if (this.debounceTimer !== null) {
            clearTimeout(this.debounceTimer);
        }

        // If value hasn't changed, return cached result
        if (value === this.lastValidatedValue && !immediate) {
            return this.lastError;
        }

        // Set debounced validation
        return new Promise((resolve) => {
            const executeValidation = async () => {
                this.isValidating = true;
                try {
                    const error = await this.executeValidation(value);
                    this.lastError = error;
                    this.lastValidatedValue = value;

                    if (error) {
                        this.onError?.(this.fieldName, error);
                    } else {
                        this.onSuccess?.(this.fieldName);
                    }

                    resolve(error);
                } finally {
                    this.isValidating = false;
                }
            };

            if (immediate) {
                executeValidation();
            } else {
                this.debounceTimer = window.setTimeout(executeValidation, this.debounceMs);
            }
        });
    }

    /**
     * Execute validation request
     */
    private async executeValidation(value: string): Promise<ValidationError | null> {
        if (!value) {
            return null; // Skip validation for empty values
        }

        try {
            const response = await fetch(this.endpoint, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-Requested-With': 'XMLHttpRequest'
                },
                body: JSON.stringify({ value })
            });

            if (!response.ok) {
                return {
                    field: this.fieldName,
                    message: 'Validation check failed',
                    code: 'VALIDATION_CHECK_FAILED',
                    severity: 'error'
                };
            }

            const result = await response.json() as any;

            // Check if validation passed
            if (result.isValid !== false) {
                return null;
            }

            // Return error if validation failed
            return {
                field: this.fieldName,
                message: result.message || 'Validation failed',
                code: result.code || 'ASYNC_VALIDATION_ERROR',
                severity: 'error'
            };
        } catch (error) {
            return {
                field: this.fieldName,
                message: error instanceof Error ? error.message : 'Validation check error',
                code: 'VALIDATION_CHECK_ERROR',
                severity: 'error'
            };
        }
    }

    /**
     * Check if currently validating
     */
    isValidating(): boolean {
        return this.isValidating;
    }

    /**
     * Get last error
     */
    getLastError(): ValidationError | null {
        return this.lastError;
    }

    /**
     * Clear last error
     */
    clearError(): void {
        this.lastError = null;
        this.lastValidatedValue = '';
    }

    /**
     * Check if field has error
     */
    hasError(): boolean {
        return this.lastError !== null;
    }
}

/**
 * AsyncValidatorGroup - Manage multiple async validators
 */
export class AsyncValidatorGroup {
    private validators: Map<string, AsyncValidator> = new Map();

    /**
     * Add validator for field
     */
    addValidator(fieldName: string, endpoint: string, debounceMs?: number): AsyncValidator {
        const validator = new AsyncValidator(fieldName, endpoint, debounceMs);
        this.validators.set(fieldName, validator);
        return validator;
    }

    /**
     * Get validator for field
     */
    getValidator(fieldName: string): AsyncValidator | undefined {
        return this.validators.get(fieldName);
    }

    /**
     * Validate field
     */
    async validateField(fieldName: string, value: string): Promise<ValidationError | null> {
        const validator = this.validators.get(fieldName);
        if (!validator) return null;
        return validator.validate(value);
    }

    /**
     * Validate all fields
     */
    async validateAll(formData: Record<string, string>): Promise<ValidationError[]> {
        const errors: ValidationError[] = [];

        const promises = Array.from(this.validators.entries()).map(async ([fieldName, validator]) => {
            const value = formData[fieldName] || '';
            const error = await validator.validate(value);
            if (error) {
                errors.push(error);
            }
        });

        await Promise.all(promises);
        return errors;
    }

    /**
     * Clear all errors
     */
    clearAll(): void {
        this.validators.forEach(validator => validator.clearError());
    }

    /**
     * Check if any field has error
     */
    hasErrors(): boolean {
        return Array.from(this.validators.values()).some(v => v.hasError());
    }

    /**
     * Get error count
     */
    getErrorCount(): number {
        return Array.from(this.validators.values()).filter(v => v.hasError()).length;
    }
}

// Export for global use
if (typeof window !== 'undefined') {
    (window as any).AsyncValidator = AsyncValidator;
    (window as any).AsyncValidatorGroup = AsyncValidatorGroup;
}
