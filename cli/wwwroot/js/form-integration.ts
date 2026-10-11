/**
 * FormIntegration - Orchestrates complete form validation workflow
 * Integrates form validator, submission handler, error display, and notifications
 */

import { FormValidator } from './form-validator';
import { FormSubmissionHandler, SubmissionOptions, ValidationResponse } from './form-submission-handler';
import { ErrorDisplay, ErrorDisplayOptions } from './error-display';
import { ValidationSummary, ValidationSummaryOptions } from './validation-summary';
import { ToastNotification, ToastType } from './toast-notification';

export interface FormIntegrationOptions {
    validateOnBlur?: boolean;
    validateOnChange?: boolean;
    validateOnSubmit?: boolean;
    showErrorSummary?: boolean;
    showToastNotifications?: boolean;
    autoSubmit?: boolean;
    submissionEndpoint?: string;
    submissionMethod?: 'POST' | 'PUT' | 'PATCH';
    focusFirstError?: boolean;
    scrollToError?: boolean;
}

export class FormIntegration {
    private formElement: HTMLFormElement;
    private validator: FormValidator;
    private submissionHandler: FormSubmissionHandler;
    private errorDisplay: ErrorDisplay;
    private validationSummary: ValidationSummary;
    private toastNotifications: ToastNotification;
    private options: FormIntegrationOptions;
    private isInitialized: boolean = false;

    constructor(
        formElement: HTMLFormElement | string,
        options: FormIntegrationOptions = {}
    ) {
        this.formElement = typeof formElement === 'string'
            ? (document.querySelector(formElement) as HTMLFormElement)
            : formElement;

        this.options = {
            validateOnBlur: true,
            validateOnChange: false,
            validateOnSubmit: true,
            showErrorSummary: true,
            showToastNotifications: true,
            autoSubmit: false,
            focusFirstError: true,
            scrollToError: true,
            ...options
        };

        // Initialize components
        this.validator = new FormValidator(this.formElement, {
            validateOnBlur: this.options.validateOnBlur,
            validateOnChange: this.options.validateOnChange,
            validateOnSubmit: false, // We'll handle submit manually
            showErrors: false // We'll use ErrorDisplay instead
        });

        this.submissionHandler = new FormSubmissionHandler(this.formElement, this.validator);
        this.errorDisplay = new ErrorDisplay(this.formElement, {
            focusFirstError: this.options.focusFirstError,
            scrollToError: this.options.scrollToError
        });
        this.validationSummary = new ValidationSummary(this.formElement, {
            autoCreate: this.options.showErrorSummary
        });
        this.toastNotifications = new ToastNotification({
            duration: 5000,
            position: 'top-end'
        });

        this.initialize();
    }

    /**
     * Initialize form integration
     */
    private initialize(): void {
        if (this.isInitialized) return;

        // Handle form submission
        this.formElement.addEventListener('submit', (e) => this.handleSubmit(e));

        this.isInitialized = true;
    }

    /**
     * Handle form submission
     */
    private async handleSubmit(event: SubmitEvent): Promise<void> {
        event.preventDefault();

        if (!this.options.submissionEndpoint) {
            console.warn('No submission endpoint configured');
            return;
        }

        // Clear previous errors
        this.errorDisplay.clearErrors();
        this.validationSummary.clear();

        // Show loading state
        this.setSubmitButtonState(true);

        try {
            // Submit form
            const response = await this.submissionHandler.submit(
                this.options.submissionEndpoint,
                {
                    method: this.options.submissionMethod || 'POST',
                    validateOnClient: true,
                    timeout: 30000,
                    retryCount: 1
                }
            );

            // Handle response
            this.handleResponse(response);
        } catch (error) {
            const message = error instanceof Error ? error.message : 'An error occurred';
            this.showError('Form submission failed: ' + message);
        } finally {
            this.setSubmitButtonState(false);
        }
    }

    /**
     * Handle submission response
     */
    private handleResponse(response: ValidationResponse): void {
        if (response.isValid) {
            // Success
            this.showSuccess(response.message || 'Form submitted successfully');

            // Call optional callback
            if (this.options.autoSubmit) {
                // Auto-submit the form (actual POST)
                this.formElement.submit();
            }
        } else {
            // Validation failed
            this.errorDisplay.displayResponse(response);
            this.validationSummary.displayResponse(response);

            if (this.options.showToastNotifications) {
                this.showError(response.message || 'Please correct the errors below');
            }
        }
    }

    /**
     * Show success message
     */
    private showSuccess(message: string): void {
        if (this.options.showToastNotifications) {
            this.toastNotifications.success(message);
        }
    }

    /**
     * Show error message
     */
    private showError(message: string): void {
        if (this.options.showToastNotifications) {
            this.toastNotifications.error(message);
        }
    }

    /**
     * Set submit button state
     */
    private setSubmitButtonState(isSubmitting: boolean): void {
        const submitButton = this.formElement.querySelector(
            'button[type="submit"]'
        ) as HTMLButtonElement;

        if (submitButton) {
            submitButton.disabled = isSubmitting;
            if (isSubmitting) {
                submitButton.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Submitting...';
            } else {
                submitButton.innerHTML = '<i class="bi bi-check-circle me-2"></i>Submit';
            }
        }
    }

    /**
     * Get form validator
     */
    getValidator(): FormValidator {
        return this.validator;
    }

    /**
     * Get submission handler
     */
    getSubmissionHandler(): FormSubmissionHandler {
        return this.submissionHandler;
    }

    /**
     * Get error display
     */
    getErrorDisplay(): ErrorDisplay {
        return this.errorDisplay;
    }

    /**
     * Get validation summary
     */
    getValidationSummary(): ValidationSummary {
        return this.validationSummary;
    }

    /**
     * Get toast notifications
     */
    getToastNotifications(): ToastNotification {
        return this.toastNotifications;
    }

    /**
     * Validate form (client-side only)
     */
    async validateForm(): Promise<boolean> {
        const errors = await this.validator.validateAll();
        if (errors.length > 0) {
            this.errorDisplay.clearErrors();
            errors.forEach(error => {
                this.errorDisplay.highlightField(error.field, error.message, error.code);
            });
        }
        return errors.length === 0;
    }

    /**
     * Submit form
     */
    async submitForm(): Promise<ValidationResponse | undefined> {
        if (!this.options.submissionEndpoint) {
            console.warn('No submission endpoint configured');
            return;
        }

        return this.submissionHandler.submit(this.options.submissionEndpoint);
    }

    /**
     * Reset form
     */
    resetForm(): void {
        this.formElement.reset();
        this.validator.reset();
        this.errorDisplay.clearErrors();
        this.validationSummary.clear();
        this.toastNotifications.clearAll();
    }

    /**
     * Clear all errors
     */
    clearErrors(): void {
        this.errorDisplay.clearErrors();
        this.validationSummary.clear();
    }

    /**
     * Show specific field error
     */
    showFieldError(fieldName: string, message: string, code?: string): void {
        this.errorDisplay.highlightField(fieldName, message, code);
    }

    /**
     * Clear specific field error
     */
    clearFieldError(fieldName: string): void {
        this.errorDisplay.clearFieldErrors(fieldName);
    }

    /**
     * Show toast notification
     */
    showNotification(message: string, type: ToastType = 'info'): void {
        this.toastNotifications.show(message, type);
    }

    /**
     * Set submission endpoint
     */
    setSubmissionEndpoint(endpoint: string): void {
        this.options.submissionEndpoint = endpoint;
    }

    /**
     * Get form data
     */
    getFormData(): Record<string, any> {
        return this.submissionHandler.getFormData();
    }

    /**
     * Check if form is valid
     */
    isValid(): boolean {
        return this.validator.isValid();
    }

    /**
     * Check if form is submitting
     */
    isSubmitting(): boolean {
        return this.submissionHandler.isSubmittingForm();
    }
}

// Export for global use
if (typeof window !== 'undefined') {
    (window as any).FormIntegration = FormIntegration;
}
