/**
 * FieldValidator - Individual field validation rules
 * Supports string, numeric, email, URL, and custom validators
 */

export interface ValidationError {
    field: string;
    message: string;
    code: string;
    severity: 'error' | 'warning' | 'info';
}

export interface ValidationRule {
    validate(value: string): ValidationError | null;
}

/**
 * Base field validator
 */
export class FieldValidator {
    fieldName: string;
    type: string;
    required: boolean = false;
    minLength?: number;
    maxLength?: number;
    pattern?: string;
    min?: number;
    max?: number;
    customRules: ValidationRule[] = [];

    constructor(fieldName: string, type: string = 'text') {
        this.fieldName = fieldName;
        this.type = type;
    }

    /**
     * Validate field value
     */
    validate(value: string): ValidationError[] {
        const errors: ValidationError[] = [];

        // Check required
        if (this.required && this.isEmpty(value)) {
            errors.push({
                field: this.fieldName,
                message: `${this.fieldName} is required`,
                code: 'REQUIRED',
                severity: 'error'
            });
            return errors;
        }

        // Skip other validations if empty and not required
        if (this.isEmpty(value)) {
            return errors;
        }

        // Type-specific validation
        switch (this.type) {
            case 'email':
                const emailError = this.validateEmail(value);
                if (emailError) errors.push(emailError);
                break;

            case 'url':
                const urlError = this.validateUrl(value);
                if (urlError) errors.push(urlError);
                break;

            case 'number':
            case 'decimal':
                const numError = this.validateNumber(value);
                if (numError) errors.push(numError);
                break;

            case 'text':
            case 'textarea':
            default:
                const textError = this.validateText(value);
                if (textError) errors.push(textError);
                break;
        }

        // Custom rules
        for (const rule of this.customRules) {
            const error = rule.validate(value);
            if (error) errors.push(error);
        }

        return errors;
    }

    /**
     * Check if value is empty
     */
    private isEmpty(value: string): boolean {
        return value === null || value === undefined || value.trim() === '';
    }

    /**
     * Validate text field
     */
    private validateText(value: string): ValidationError | null {
        // Check length
        if (this.minLength && value.length < this.minLength) {
            return {
                field: this.fieldName,
                message: `${this.fieldName} must be at least ${this.minLength} characters`,
                code: 'LENGTH_ERROR',
                severity: 'error'
            };
        }

        if (this.maxLength && value.length > this.maxLength) {
            return {
                field: this.fieldName,
                message: `${this.fieldName} must be at most ${this.maxLength} characters`,
                code: 'LENGTH_ERROR',
                severity: 'error'
            };
        }

        // Check pattern
        if (this.pattern) {
            const regex = new RegExp(this.pattern);
            if (!regex.test(value)) {
                return {
                    field: this.fieldName,
                    message: `${this.fieldName} has an invalid format`,
                    code: 'INVALID_FORMAT',
                    severity: 'error'
                };
            }
        }

        return null;
    }

    /**
     * Validate email
     */
    private validateEmail(value: string): ValidationError | null {
        const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
        if (!emailRegex.test(value)) {
            return {
                field: this.fieldName,
                message: `${this.fieldName} must be a valid email address`,
                code: 'INVALID_FORMAT',
                severity: 'error'
            };
        }
        return null;
    }

    /**
     * Validate URL
     */
    private validateUrl(value: string): ValidationError | null {
        try {
            new URL(value);
            return null;
        } catch {
            return {
                field: this.fieldName,
                message: `${this.fieldName} must be a valid URL`,
                code: 'INVALID_FORMAT',
                severity: 'error'
            };
        }
    }

    /**
     * Validate number
     */
    private validateNumber(value: string): ValidationError | null {
        const num = parseFloat(value);

        if (isNaN(num)) {
            return {
                field: this.fieldName,
                message: `${this.fieldName} must be a valid number`,
                code: 'INVALID_FORMAT',
                severity: 'error'
            };
        }

        if (this.min !== undefined && num < this.min) {
            return {
                field: this.fieldName,
                message: `${this.fieldName} must be greater than or equal to ${this.min}`,
                code: 'RANGE_ERROR',
                severity: 'error'
            };
        }

        if (this.max !== undefined && num > this.max) {
            return {
                field: this.fieldName,
                message: `${this.fieldName} must be less than or equal to ${this.max}`,
                code: 'RANGE_ERROR',
                severity: 'error'
            };
        }

        return null;
    }

    /**
     * Add custom validation rule
     */
    addRule(rule: ValidationRule): FieldValidator {
        this.customRules.push(rule);
        return this;
    }

    /**
     * Set required validation
     */
    setRequired(required: boolean): FieldValidator {
        this.required = required;
        return this;
    }

    /**
     * Set length constraints
     */
    setLength(min?: number, max?: number): FieldValidator {
        this.minLength = min;
        this.maxLength = max;
        return this;
    }

    /**
     * Set numeric range
     */
    setRange(min?: number, max?: number): FieldValidator {
        this.min = min;
        this.max = max;
        return this;
    }

    /**
     * Set pattern for text validation
     */
    setPattern(pattern: string): FieldValidator {
        this.pattern = pattern;
        return this;
    }
}

/**
 * String validator rules
 */
export class StringValidationRule implements ValidationRule {
    constructor(
        private minLength?: number,
        private maxLength?: number,
        private pattern?: string
    ) {}

    validate(value: string): ValidationError | null {
        if (this.minLength && value.length < this.minLength) {
            return {
                field: 'unknown',
                message: `Must be at least ${this.minLength} characters`,
                code: 'LENGTH_ERROR',
                severity: 'error'
            };
        }

        if (this.maxLength && value.length > this.maxLength) {
            return {
                field: 'unknown',
                message: `Must be at most ${this.maxLength} characters`,
                code: 'LENGTH_ERROR',
                severity: 'error'
            };
        }

        if (this.pattern) {
            const regex = new RegExp(this.pattern);
            if (!regex.test(value)) {
                return {
                    field: 'unknown',
                    message: 'Invalid format',
                    code: 'INVALID_FORMAT',
                    severity: 'error'
                };
            }
        }

        return null;
    }
}

/**
 * Numeric validator rules
 */
export class NumericValidationRule implements ValidationRule {
    constructor(
        private min?: number,
        private max?: number
    ) {}

    validate(value: string): ValidationError | null {
        const num = parseFloat(value);

        if (isNaN(num)) {
            return {
                field: 'unknown',
                message: 'Must be a valid number',
                code: 'INVALID_FORMAT',
                severity: 'error'
            };
        }

        if (this.min !== undefined && num < this.min) {
            return {
                field: 'unknown',
                message: `Must be greater than or equal to ${this.min}`,
                code: 'RANGE_ERROR',
                severity: 'error'
            };
        }

        if (this.max !== undefined && num > this.max) {
            return {
                field: 'unknown',
                message: `Must be less than or equal to ${this.max}`,
                code: 'RANGE_ERROR',
                severity: 'error'
            };
        }

        return null;
    }
}

/**
 * Custom validation rule
 */
export class CustomValidationRule implements ValidationRule {
    constructor(
        private validatorFn: (value: string) => boolean,
        private message: string,
        private code: string = 'CUSTOM_ERROR'
    ) {}

    validate(value: string): ValidationError | null {
        if (!this.validatorFn(value)) {
            return {
                field: 'unknown',
                message: this.message,
                code: this.code,
                severity: 'error'
            };
        }
        return null;
    }
}

// Export for global use
if (typeof window !== 'undefined') {
    (window as any).FieldValidator = FieldValidator;
    (window as any).StringValidationRule = StringValidationRule;
    (window as any).NumericValidationRule = NumericValidationRule;
    (window as any).CustomValidationRule = CustomValidationRule;
}
