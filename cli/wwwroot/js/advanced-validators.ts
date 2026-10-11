/**
 * AdvancedValidators - Complex validation rules
 * Password strength, file uploads, credit cards, phone numbers, etc.
 */

import { ValidationError, ValidationRule } from './field-validators';

/**
 * Password Strength Validator
 */
export class PasswordStrengthValidator implements ValidationRule {
    private fieldName: string;
    private requireUppercase: boolean = false;
    private requireLowercase: boolean = boolean = true;
    private requireNumbers: boolean = false;
    private requireSpecial: boolean = false;
    private minLength: number = 8;

    constructor(fieldName: string = 'password') {
        this.fieldName = fieldName;
    }

    /**
     * Require uppercase letters
     */
    withUppercase(): PasswordStrengthValidator {
        this.requireUppercase = true;
        return this;
    }

    /**
     * Require lowercase letters (default: true)
     */
    withLowercase(): PasswordStrengthValidator {
        this.requireLowercase = true;
        return this;
    }

    /**
     * Require numbers
     */
    withNumbers(): PasswordStrengthValidator {
        this.requireNumbers = true;
        return this;
    }

    /**
     * Require special characters
     */
    withSpecial(): PasswordStrengthValidator {
        this.requireSpecial = true;
        return this;
    }

    /**
     * Set minimum length
     */
    setMinLength(length: number): PasswordStrengthValidator {
        this.minLength = length;
        return this;
    }

    /**
     * Validate password strength
     */
    validate(value: string): ValidationError | null {
        if (!value) {
            return null;
        }

        const checks = [];

        // Check length
        if (value.length < this.minLength) {
            checks.push(`at least ${this.minLength} characters`);
        }

        // Check uppercase
        if (this.requireUppercase && !/[A-Z]/.test(value)) {
            checks.push('at least one uppercase letter');
        }

        // Check lowercase
        if (this.requireLowercase && !/[a-z]/.test(value)) {
            checks.push('at least one lowercase letter');
        }

        // Check numbers
        if (this.requireNumbers && !/[0-9]/.test(value)) {
            checks.push('at least one number');
        }

        // Check special characters
        if (this.requireSpecial && !/[!@#$%^&*()_+\-=[\]{};':"\\|,.<>/?]/.test(value)) {
            checks.push('at least one special character');
        }

        if (checks.length > 0) {
            return {
                field: this.fieldName,
                message: `Password must contain ${checks.join(', ')}`,
                code: 'WEAK_PASSWORD',
                severity: 'error'
            };
        }

        return null;
    }

    /**
     * Get password strength score (0-100)
     */
    getStrength(value: string): number {
        let score = 0;

        if (value.length >= this.minLength) score += 20;
        if (value.length >= this.minLength + 5) score += 10;
        if (/[a-z]/.test(value)) score += 15;
        if (/[A-Z]/.test(value)) score += 15;
        if (/[0-9]/.test(value)) score += 20;
        if (/[!@#$%^&*()_+\-=[\]{};':"\\|,.<>/?]/.test(value)) score += 20;

        return Math.min(score, 100);
    }
}

/**
 * File Upload Validator
 */
export class FileUploadValidator implements ValidationRule {
    private fieldName: string;
    private maxSizeBytes: number = 5 * 1024 * 1024; // 5MB
    private allowedTypes: string[] = [];
    private allowedExtensions: string[] = [];

    constructor(fieldName: string = 'file') {
        this.fieldName = fieldName;
    }

    /**
     * Set maximum file size
     */
    setMaxSize(bytes: number): FileUploadValidator {
        this.maxSizeBytes = bytes;
        return this;
    }

    /**
     * Allow specific MIME types
     */
    allowMimeTypes(...types: string[]): FileUploadValidator {
        this.allowedTypes = types;
        return this;
    }

    /**
     * Allow specific file extensions
     */
    allowExtensions(...extensions: string[]): FileUploadValidator {
        this.allowedExtensions = extensions.map(ext => ext.toLowerCase());
        return this;
    }

    /**
     * Validate file
     */
    validate(value: File | string): ValidationError | null {
        if (!value) {
            return null;
        }

        if (typeof value === 'string') {
            return null; // Can't validate string file path
        }

        const file = value as File;

        // Check file size
        if (file.size > this.maxSizeBytes) {
            const maxSizeMB = (this.maxSizeBytes / (1024 * 1024)).toFixed(1);
            return {
                field: this.fieldName,
                message: `File size must not exceed ${maxSizeMB}MB`,
                code: 'FILE_TOO_LARGE',
                severity: 'error'
            };
        }

        // Check MIME type
        if (this.allowedTypes.length > 0 && !this.allowedTypes.includes(file.type)) {
            return {
                field: this.fieldName,
                message: `File type must be one of: ${this.allowedTypes.join(', ')}`,
                code: 'INVALID_FILE_TYPE',
                severity: 'error'
            };
        }

        // Check extension
        if (this.allowedExtensions.length > 0) {
            const fileExt = this.getFileExtension(file.name).toLowerCase();
            if (!this.allowedExtensions.includes(fileExt)) {
                return {
                    field: this.fieldName,
                    message: `File extension must be one of: ${this.allowedExtensions.join(', ')}`,
                    code: 'INVALID_FILE_EXTENSION',
                    severity: 'error'
                };
            }
        }

        return null;
    }

    /**
     * Get file extension
     */
    private getFileExtension(filename: string): string {
        const parts = filename.split('.');
        return parts.length > 1 ? parts[parts.length - 1] : '';
    }
}

/**
 * Phone Number Validator
 */
export class PhoneNumberValidator implements ValidationRule {
    private fieldName: string;

    constructor(fieldName: string = 'phone') {
        this.fieldName = fieldName;
    }

    /**
     * Validate phone number (basic international format)
     */
    validate(value: string): ValidationError | null {
        if (!value) {
            return null;
        }

        // Remove common separators
        const cleaned = value.replace(/[\s\-\.\(\)]/g, '');

        // Check if it's a valid phone number (at least 7 digits)
        if (!/^\+?[0-9]{7,15}$/.test(cleaned)) {
            return {
                field: this.fieldName,
                message: 'Please enter a valid phone number',
                code: 'INVALID_PHONE',
                severity: 'error'
            };
        }

        return null;
    }
}

/**
 * Credit Card Validator
 */
export class CreditCardValidator implements ValidationRule {
    private fieldName: string;

    constructor(fieldName: string = 'cardNumber') {
        this.fieldName = fieldName;
    }

    /**
     * Validate credit card using Luhn algorithm
     */
    validate(value: string): ValidationError | null {
        if (!value) {
            return null;
        }

        const cleaned = value.replace(/\s/g, '');

        // Check format
        if (!/^\d{13,19}$/.test(cleaned)) {
            return {
                field: this.fieldName,
                message: 'Please enter a valid card number',
                code: 'INVALID_CARD_NUMBER',
                severity: 'error'
            };
        }

        // Luhn algorithm check
        if (!this.luhnCheck(cleaned)) {
            return {
                field: this.fieldName,
                message: 'Card number is invalid',
                code: 'INVALID_CARD_CHECKSUM',
                severity: 'error'
            };
        }

        return null;
    }

    /**
     * Luhn algorithm for card validation
     */
    private luhnCheck(cardNumber: string): boolean {
        let sum = 0;
        let isEven = false;

        for (let i = cardNumber.length - 1; i >= 0; i--) {
            let digit = parseInt(cardNumber.charAt(i), 10);

            if (isEven) {
                digit *= 2;
                if (digit > 9) {
                    digit -= 9;
                }
            }

            sum += digit;
            isEven = !isEven;
        }

        return sum % 10 === 0;
    }
}

/**
 * URL Slug Validator
 */
export class SlugValidator implements ValidationRule {
    private fieldName: string;

    constructor(fieldName: string = 'slug') {
        this.fieldName = fieldName;
    }

    /**
     * Validate URL slug format
     */
    validate(value: string): ValidationError | null {
        if (!value) {
            return null;
        }

        // Slug must be lowercase, alphanumeric, and hyphens only
        if (!/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(value)) {
            return {
                field: this.fieldName,
                message: 'Slug must contain only lowercase letters, numbers, and hyphens',
                code: 'INVALID_SLUG',
                severity: 'error'
            };
        }

        // Must not start or end with hyphen
        if (value.startsWith('-') || value.endsWith('-')) {
            return {
                field: this.fieldName,
                message: 'Slug cannot start or end with a hyphen',
                code: 'INVALID_SLUG_FORMAT',
                severity: 'error'
            };
        }

        // Must not have consecutive hyphens
        if (value.includes('--')) {
            return {
                field: this.fieldName,
                message: 'Slug cannot contain consecutive hyphens',
                code: 'INVALID_SLUG_HYPHENS',
                severity: 'error'
            };
        }

        return null;
    }
}

// Export for global use
if (typeof window !== 'undefined') {
    (window as any).PasswordStrengthValidator = PasswordStrengthValidator;
    (window as any).FileUploadValidator = FileUploadValidator;
    (window as any).PhoneNumberValidator = PhoneNumberValidator;
    (window as any).CreditCardValidator = CreditCardValidator;
    (window as any).SlugValidator = SlugValidator;
}
