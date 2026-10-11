/**
 * ErrorDisplay - Display validation errors in the UI
 * Highlights fields, shows error messages, and manages error state
 */

import { ValidationResponse, FieldError } from './form-submission-handler';

export interface ErrorDisplayOptions {
    errorClass?: string;
    successClass?: string;
    containerClass?: string;
    messageClass?: string;
    focusFirstError?: boolean;
    scrollToError?: boolean;
}

export class ErrorDisplay {
    private formElement: HTMLFormElement;
    private displayedErrors: Map<string, string[]> = new Map();
    private options: ErrorDisplayOptions;

    constructor(
        formElement: HTMLFormElement | string,
        options: ErrorDisplayOptions = {}
    ) {
        this.formElement = typeof formElement === 'string'
            ? (document.querySelector(formElement) as HTMLFormElement)
            : formElement;

        this.options = {
            errorClass: 'is-invalid',
            successClass: 'is-valid',
            containerClass: 'form-group',
            messageClass: 'invalid-feedback d-block',
            focusFirstError: true,
            scrollToError: true,
            ...options
        };
    }

    /**
     * Display validation response errors
     */
    displayResponse(response: ValidationResponse): void {
        this.clearErrors();

        if (response.isValid) {
            this.displaySuccess();
            return;
        }

        // Group errors by field
        const errorsByField: Map<string, FieldError[]> = new Map();
        response.errors.forEach(error => {
            const errors = errorsByField.get(error.field) || [];
            errors.push(error);
            errorsByField.set(error.field, errors);
        });

        // Display each field's errors
        let firstErrorField: string | null = null;
        errorsByField.forEach((errors, fieldName) => {
            this.displayFieldErrors(fieldName, errors);
            if (!firstErrorField) {
                firstErrorField = fieldName;
            }
        });

        // Display general errors
        const generalErrors = response.errors.filter(e => e.field === 'general');
        if (generalErrors.length > 0) {
            this.displayGeneralErrors(generalErrors);
        }

        // Focus first error field
        if (this.options.focusFirstError && firstErrorField) {
            this.focusField(firstErrorField);
        }

        // Scroll to first error
        if (this.options.scrollToError && firstErrorField) {
            this.scrollToField(firstErrorField);
        }
    }

    /**
     * Display errors for a specific field
     */
    private displayFieldErrors(fieldName: string, errors: FieldError[]): void {
        const field = this.formElement.querySelector(`[name="${fieldName}"]`) as
            HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;

        if (!field) {
            console.warn(`Field "${fieldName}" not found in form`);
            return;
        }

        const container = field.closest(`.${this.options.containerClass}`) || field.parentElement;
        if (!container) {
            return;
        }

        // Add error class
        field.classList.remove(this.options.successClass!);
        field.classList.add(this.options.errorClass!);

        // Remove existing error messages
        const existingMessages = container.querySelectorAll(`.${this.options.messageClass}`);
        existingMessages.forEach(msg => msg.remove());

        // Add error messages
        errors.forEach(error => {
            const messageDiv = document.createElement('div');
            messageDiv.className = this.options.messageClass!;
            messageDiv.textContent = error.message;
            messageDiv.setAttribute('data-error-code', error.code || 'UNKNOWN');
            container.appendChild(messageDiv);
        });

        // Store displayed errors
        this.displayedErrors.set(fieldName, errors.map(e => e.message));
    }

    /**
     * Display general errors
     */
    private displayGeneralErrors(errors: FieldError[]): void {
        const summaryContainer = this.formElement.querySelector('.validation-summary-container');

        if (summaryContainer) {
            const alert = summaryContainer.querySelector('.alert');
            if (alert) {
                alert.classList.remove('d-none');
            }
        }
    }

    /**
     * Display success state
     */
    private displaySuccess(): void {
        this.formElement.querySelectorAll('input, textarea, select').forEach(field => {
            field.classList.remove(this.options.errorClass!);
            field.classList.add(this.options.successClass!);
        });

        const summaryContainer = this.formElement.querySelector('.validation-summary-container');
        if (summaryContainer) {
            const alert = summaryContainer.querySelector('.alert');
            if (alert) {
                alert.classList.add('d-none');
            }
        }
    }

    /**
     * Clear all displayed errors
     */
    clearErrors(): void {
        this.displayedErrors.clear();

        // Remove error classes
        this.formElement.querySelectorAll(`.${this.options.errorClass}`).forEach(element => {
            element.classList.remove(this.options.errorClass!);
        });

        // Remove error messages
        this.formElement.querySelectorAll(`.${this.options.messageClass}`).forEach(element => {
            element.remove();
        });

        // Hide validation summary
        const summaryContainer = this.formElement.querySelector('.validation-summary-container');
        if (summaryContainer) {
            const alert = summaryContainer.querySelector('.alert');
            if (alert) {
                alert.classList.add('d-none');
            }
        }
    }

    /**
     * Focus field and scroll into view
     */
    private focusField(fieldName: string): void {
        const field = this.formElement.querySelector(`[name="${fieldName}"]`) as
            HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;

        if (field) {
            field.focus();
        }
    }

    /**
     * Scroll to field
     */
    private scrollToField(fieldName: string): void {
        const field = this.formElement.querySelector(`[name="${fieldName}"]`);

        if (field) {
            field.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
    }

    /**
     * Get displayed errors
     */
    getDisplayedErrors(): Map<string, string[]> {
        return this.displayedErrors;
    }

    /**
     * Check if field has errors
     */
    hasErrors(fieldName?: string): boolean {
        if (fieldName) {
            return this.displayedErrors.has(fieldName);
        }
        return this.displayedErrors.size > 0;
    }

    /**
     * Get error count
     */
    getErrorCount(): number {
        let count = 0;
        this.displayedErrors.forEach(messages => {
            count += messages.length;
        });
        return count;
    }

    /**
     * Highlight field with error style
     */
    highlightField(fieldName: string, message: string, code?: string): void {
        const field = this.formElement.querySelector(`[name="${fieldName}"]`) as
            HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;

        if (!field) return;

        const container = field.closest(`.${this.options.containerClass}`) || field.parentElement;
        if (!container) return;

        field.classList.add(this.options.errorClass!);
        field.classList.remove(this.options.successClass!);

        const messageDiv = document.createElement('div');
        messageDiv.className = this.options.messageClass!;
        messageDiv.textContent = message;
        if (code) {
            messageDiv.setAttribute('data-error-code', code);
        }
        container.appendChild(messageDiv);

        const messages = this.displayedErrors.get(fieldName) || [];
        messages.push(message);
        this.displayedErrors.set(fieldName, messages);
    }

    /**
     * Clear field errors
     */
    clearFieldErrors(fieldName: string): void {
        const field = this.formElement.querySelector(`[name="${fieldName}"]`) as
            HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;

        if (!field) return;

        const container = field.closest(`.${this.options.containerClass}`) || field.parentElement;
        if (!container) return;

        field.classList.remove(this.options.errorClass!);
        field.classList.add(this.options.successClass!);

        const messages = container.querySelectorAll(`.${this.options.messageClass}`);
        messages.forEach(msg => msg.remove());

        this.displayedErrors.delete(fieldName);
    }
}

// Export for global use
if (typeof window !== 'undefined') {
    (window as any).ErrorDisplay = ErrorDisplay;
}
