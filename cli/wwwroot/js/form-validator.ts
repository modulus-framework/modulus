/**
 * FormValidator - Client-side form validation orchestrator
 * Supports real-time field validation, form-level validation, and error display
 */

import { FieldValidator, ValidationError } from './field-validators';
import { ValidationMessages } from './validation-messages';

export interface FormValidationOptions {
    validateOnBlur?: boolean;
    validateOnChange?: boolean;
    validateOnSubmit?: boolean;
    showErrors?: boolean;
    errorClass?: string;
    successClass?: string;
}

export class FormValidator {
    private formElement: HTMLFormElement;
    private fieldValidators: Map<string, FieldValidator> = new Map();
    private fieldErrors: Map<string, ValidationError[]> = new Map();
    private options: FormValidationOptions;
    private messages: ValidationMessages;

    constructor(
        formElement: HTMLFormElement | string,
        options: Partial<FormValidationOptions> = {}
    ) {
        this.formElement = typeof formElement === 'string'
            ? (document.querySelector(formElement) as HTMLFormElement)
            : formElement;

        this.options = {
            validateOnBlur: true,
            validateOnChange: false,
            validateOnSubmit: true,
            showErrors: true,
            errorClass: 'is-invalid',
            successClass: 'is-valid',
            ...options
        };

        this.messages = new ValidationMessages();
        this.initialize();
    }

    /**
     * Initialize form validation
     */
    private initialize(): void {
        if (!this.formElement) {
            console.error('FormValidator: Form element not found');
            return;
        }

        // Get all form fields
        const fields = this.formElement.querySelectorAll(
            'input[data-validator], textarea[data-validator], select[data-validator]'
        );

        fields.forEach((field: Element) => {
            const htmlField = field as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;
            const validatorType = htmlField.dataset.validator;

            if (validatorType) {
                const fieldName = htmlField.name || htmlField.id;
                const validator = new FieldValidator(fieldName, validatorType);

                // Copy validation attributes
                if (htmlField.dataset.required !== undefined) {
                    validator.required = true;
                }
                if (htmlField.dataset.minLength) {
                    validator.minLength = parseInt(htmlField.dataset.minLength, 10);
                }
                if (htmlField.dataset.maxLength) {
                    validator.maxLength = parseInt(htmlField.dataset.maxLength, 10);
                }
                if (htmlField.dataset.pattern) {
                    validator.pattern = htmlField.dataset.pattern;
                }
                if (htmlField.dataset.min) {
                    validator.min = parseFloat(htmlField.dataset.min);
                }
                if (htmlField.dataset.max) {
                    validator.max = parseFloat(htmlField.dataset.max);
                }

                this.fieldValidators.set(fieldName, validator);

                // Attach event listeners
                if (this.options.validateOnBlur) {
                    htmlField.addEventListener('blur', () => this.validateField(fieldName));
                }
                if (this.options.validateOnChange) {
                    htmlField.addEventListener('change', () => this.validateField(fieldName));
                    htmlField.addEventListener('input', () => this.validateField(fieldName));
                }
            }
        });

        // Attach form submit handler
        if (this.options.validateOnSubmit) {
            this.formElement.addEventListener('submit', (e) => this.handleSubmit(e));
        }
    }

    /**
     * Validate a single field
     */
    validateField(fieldName: string): ValidationError[] {
        const field = this.formElement.elements.namedItem(fieldName) as HTMLInputElement;
        const validator = this.fieldValidators.get(fieldName);

        if (!field || !validator) {
            return [];
        }

        const errors = validator.validate(field.value);
        this.fieldErrors.set(fieldName, errors);

        if (this.options.showErrors) {
            this.displayFieldErrors(field, errors);
        }

        return errors;
    }

    /**
     * Validate all form fields
     */
    async validateAll(): Promise<ValidationError[]> {
        const allErrors: ValidationError[] = [];

        for (const [fieldName, validator] of this.fieldValidators) {
            const field = this.formElement.elements.namedItem(fieldName) as HTMLInputElement;
            if (field) {
                const errors = this.validateField(fieldName);
                allErrors.push(...errors);
            }
        }

        return allErrors;
    }

    /**
     * Display errors for a field
     */
    private displayFieldErrors(field: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement, errors: ValidationError[]): void {
        const fieldGroup = field.closest('.form-group') || field.parentElement;
        if (!fieldGroup) return;

        // Remove existing error elements
        const existingErrors = fieldGroup.querySelectorAll('.invalid-feedback');
        existingErrors.forEach(el => el.remove());

        // Update field styling
        if (errors.length > 0) {
            field.classList.add(this.options.errorClass!);
            field.classList.remove(this.options.successClass!);

            // Add error messages
            errors.forEach(error => {
                const errorDiv = document.createElement('div');
                errorDiv.className = 'invalid-feedback d-block';
                errorDiv.textContent = error.message;
                fieldGroup.appendChild(errorDiv);
            });
        } else {
            field.classList.remove(this.options.errorClass!);
            field.classList.add(this.options.successClass!);
        }
    }

    /**
     * Handle form submission
     */
    private async handleSubmit(e: Event): Promise<void> {
        const submitEvent = e as SubmitEvent;
        submitEvent.preventDefault();

        const errors = await this.validateAll();

        if (errors.length === 0) {
            // Validation passed, submit form
            this.formElement.submit();
        } else {
            // Validation failed, show errors
            console.log('Form validation failed:', errors);
        }
    }

    /**
     * Register a custom field validator
     */
    registerFieldValidator(fieldName: string, validator: FieldValidator): void {
        this.fieldValidators.set(fieldName, validator);
    }

    /**
     * Clear validation errors
     */
    clearErrors(): void {
        this.fieldErrors.clear();
        const fields = this.formElement.querySelectorAll(`input, textarea, select`);
        fields.forEach(field => {
            field.classList.remove(this.options.errorClass!);
            field.classList.remove(this.options.successClass!);
        });
        const errors = this.formElement.querySelectorAll('.invalid-feedback');
        errors.forEach(el => el.remove());
    }

    /**
     * Reset form to initial state
     */
    reset(): void {
        this.formElement.reset();
        this.clearErrors();
    }

    /**
     * Get all validation errors
     */
    getErrors(): Map<string, ValidationError[]> {
        return this.fieldErrors;
    }

    /**
     * Check if form is valid
     */
    isValid(): boolean {
        return this.fieldErrors.size === 0 ||
               Array.from(this.fieldErrors.values()).every(errors => errors.length === 0);
    }
}

// Export for global use
if (typeof window !== 'undefined') {
    (window as any).FormValidator = FormValidator;
}
