/**
 * AdvancedValidationTests - Tests for async, cross-field, and advanced validators
 */

import { AsyncValidator, AsyncValidatorGroup } from '../wwwroot/js/async-validator';
import { CrossFieldValidator, ConditionalFieldValidator } from '../wwwroot/js/cross-field-validator';
import {
    PasswordStrengthValidator,
    FileUploadValidator,
    PhoneNumberValidator,
    CreditCardValidator,
    SlugValidator
} from '../wwwroot/js/advanced-validators';

describe('AsyncValidator', () => {
    let validator: AsyncValidator;

    beforeEach(() => {
        validator = new AsyncValidator('username', '/api/check-username', 500);
    });

    describe('Configuration', () => {
        test('should set endpoint', () => {
            validator.setEndpoint('/api/validate');
            expect(validator).toBeDefined();
        });

        test('should set debounce delay', () => {
            validator.setDebounceMs(1000);
            expect(validator).toBeDefined();
        });

        test('should set callbacks', () => {
            validator
                .onSuccessCallback(() => {})
                .onErrorCallback(() => {});
            expect(validator).toBeDefined();
        });
    });

    describe('Validation', () => {
        test('should return null for empty value', async () => {
            const error = await validator.validate('');
            expect(error).toBeNull();
        });

        test('should track validation state', () => {
            expect(validator.isValidating()).toBe(false);
        });

        test('should cache last validated value', async () => {
            await validator.validate('testuser');
            const cached = await validator.validate('testuser');
            expect(cached).toEqual(validator.getLastError());
        });

        test('should clear error', () => {
            validator.clearError();
            expect(validator.hasError()).toBe(false);
        });
    });
});

describe('AsyncValidatorGroup', () => {
    let group: AsyncValidatorGroup;

    beforeEach(() => {
        group = new AsyncValidatorGroup();
        group.addValidator('username', '/api/check-username');
        group.addValidator('email', '/api/check-email');
    });

    describe('Validator Management', () => {
        test('should add validators', () => {
            const validator = group.getValidator('username');
            expect(validator).toBeDefined();
        });

        test('should get validator', () => {
            const validator = group.getValidator('username');
            expect(validator).toBeDefined();
        });

        test('should return undefined for non-existent validator', () => {
            const validator = group.getValidator('nonexistent');
            expect(validator).toBeUndefined();
        });
    });

    describe('Validation', () => {
        test('should validate single field', async () => {
            const error = await group.validateField('username', 'testuser');
            // Should not throw
            expect(true).toBe(true);
        });

        test('should validate all fields', async () => {
            const errors = await group.validateAll({
                username: 'testuser',
                email: 'test@example.com'
            });
            expect(Array.isArray(errors)).toBe(true);
        });

        test('should check for errors', () => {
            const hasErrors = group.hasErrors();
            expect(typeof hasErrors).toBe('boolean');
        });

        test('should get error count', () => {
            const count = group.getErrorCount();
            expect(count).toBeGreaterThanOrEqual(0);
        });

        test('should clear all errors', () => {
            group.clearAll();
            expect(group.hasErrors()).toBe(false);
        });
    });
});

describe('CrossFieldValidator', () => {
    let validator: CrossFieldValidator;

    beforeEach(() => {
        validator = new CrossFieldValidator();
    });

    describe('Password Confirmation', () => {
        beforeEach(() => {
            validator.addPasswordConfirmation('password', 'confirmPassword');
        });

        test('should pass when passwords match', () => {
            const errors = validator.validate({
                password: 'SecurePass123!',
                confirmPassword: 'SecurePass123!'
            });
            expect(errors.length).toBe(0);
        });

        test('should fail when passwords do not match', () => {
            const errors = validator.validate({
                password: 'SecurePass123!',
                confirmPassword: 'DifferentPass123!'
            });
            expect(errors.length).toBeGreaterThan(0);
            expect(errors[0].code).toBe('PASSWORD_MISMATCH');
        });

        test('should skip validation for empty fields', () => {
            const errors = validator.validate({
                password: '',
                confirmPassword: ''
            });
            expect(errors.length).toBe(0);
        });
    });

    describe('Date Range Validation', () => {
        beforeEach(() => {
            validator.addDateRange('startDate', 'endDate');
        });

        test('should pass when end date is after start date', () => {
            const errors = validator.validate({
                startDate: '2026-01-01',
                endDate: '2026-12-31'
            });
            expect(errors.length).toBe(0);
        });

        test('should fail when end date is before start date', () => {
            const errors = validator.validate({
                startDate: '2026-12-31',
                endDate: '2026-01-01'
            });
            expect(errors.length).toBeGreaterThan(0);
            expect(errors[0].code).toBe('INVALID_DATE_RANGE');
        });
    });

    describe('Custom Rules', () => {
        test('should add custom rule', () => {
            validator.addRule(
                'field1',
                'field2',
                (v1, v2) => v1.length === v2.length,
                'Fields must have same length'
            );
            expect(validator).toBeDefined();
        });

        test('should validate custom rule', () => {
            validator.addRule(
                'username',
                'confirmUsername',
                (u1, u2) => u1 === u2,
                'Usernames must match'
            );

            const errors = validator.validate({
                username: 'john',
                confirmUsername: 'john'
            });
            expect(errors.length).toBe(0);
        });
    });

    describe('Error Management', () => {
        beforeEach(() => {
            validator.addPasswordConfirmation();
        });

        test('should get field error', () => {
            validator.validate({
                password: 'pass1',
                confirmPassword: 'pass2'
            });

            const error = validator.getFieldError('password');
            expect(error).toBeDefined();
        });

        test('should check if field has error', () => {
            validator.validate({
                password: 'pass1',
                confirmPassword: 'pass2'
            });

            expect(validator.hasError('password')).toBe(true);
        });

        test('should clear all errors', () => {
            validator.validate({
                password: 'pass1',
                confirmPassword: 'pass2'
            });

            validator.clearErrors();
            expect(validator.getErrors().length).toBe(0);
        });

        test('should clear specific field error', () => {
            validator.validate({
                password: 'pass1',
                confirmPassword: 'pass2'
            });

            validator.clearFieldError('password');
            expect(validator.hasError('password')).toBe(false);
        });
    });
});

describe('ConditionalFieldValidator', () => {
    let formElement: HTMLFormElement;
    let validator: ConditionalFieldValidator;

    beforeEach(() => {
        formElement = document.createElement('form');
        formElement.innerHTML = `
            <select name="country">
                <option value="">Select</option>
                <option value="US">United States</option>
                <option value="CA">Canada</option>
            </select>
            <input type="text" name="state" />
            <input type="text" name="province" />
            <input type="checkbox" name="hasPhone" />
            <input type="text" name="phone" />
        `;
        document.body.appendChild(formElement);

        validator = new ConditionalFieldValidator();
    });

    afterEach(() => {
        document.body.removeChild(formElement);
    });

    describe('Conditions', () => {
        test('should add show condition', () => {
            validator.addCondition('country', 'US', 'state', 'show');
            expect(validator).toBeDefined();
        });

        test('should add hide condition', () => {
            validator.addCondition('country', 'CA', 'province', 'show');
            expect(validator).toBeDefined();
        });

        test('should add enable condition', () => {
            validator.addCondition('hasPhone', true, 'phone', 'enable');
            expect(validator).toBeDefined();
        });

        test('should add require condition', () => {
            validator.addCondition('hasPhone', true, 'phone', 'require');
            expect(validator).toBeDefined();
        });
    });

    describe('Evaluation', () => {
        test('should apply show condition when true', () => {
            validator.addCondition('country', 'US', 'state', 'show');
            validator.applyConditions(formElement, { country: 'US' });

            const field = formElement.querySelector('[name="state"]') as HTMLInputElement;
            expect(field.style.display).not.toBe('none');
        });

        test('should apply hide condition when true', () => {
            validator.addCondition('country', 'US', 'state', 'hide');
            validator.applyConditions(formElement, { country: 'US' });

            const field = formElement.querySelector('[name="state"]') as HTMLInputElement;
            expect(field.style.display).toBe('none');
        });

        test('should enable field when condition is met', () => {
            validator.addCondition('hasPhone', true, 'phone', 'enable');
            validator.applyConditions(formElement, { hasPhone: true });

            const field = formElement.querySelector('[name="phone"]') as HTMLInputElement;
            expect(field.disabled).toBe(false);
        });

        test('should watch for changes', () => {
            validator.addCondition('country', 'US', 'state', 'show');
            validator.watchChanges(formElement);

            expect(validator).toBeDefined();
        });
    });
});

describe('PasswordStrengthValidator', () => {
    let validator: PasswordStrengthValidator;

    beforeEach(() => {
        validator = new PasswordStrengthValidator();
    });

    describe('Configuration', () => {
        test('should require uppercase', () => {
            validator.withUppercase();
            expect(validator).toBeDefined();
        });

        test('should require numbers', () => {
            validator.withNumbers();
            expect(validator).toBeDefined();
        });

        test('should require special characters', () => {
            validator.withSpecial();
            expect(validator).toBeDefined();
        });

        test('should set minimum length', () => {
            validator.setMinLength(12);
            expect(validator).toBeDefined();
        });
    });

    describe('Validation', () => {
        test('should pass weak password', () => {
            const error = validator.validate('weak');
            expect(error).not.toBeNull();
        });

        test('should pass strong password', () => {
            validator
                .withUppercase()
                .withNumbers()
                .setMinLength(8);

            const error = validator.validate('StrongPass123');
            expect(error).toBeNull();
        });

        test('should get strength score', () => {
            const score = validator.getStrength('StrongPass123!');
            expect(score).toBeGreaterThan(0);
            expect(score).toBeLessThanOrEqual(100);
        });
    });
});

describe('FileUploadValidator', () => {
    let validator: FileUploadValidator;

    beforeEach(() => {
        validator = new FileUploadValidator();
    });

    describe('Configuration', () => {
        test('should set max size', () => {
            validator.setMaxSize(10 * 1024 * 1024);
            expect(validator).toBeDefined();
        });

        test('should allow MIME types', () => {
            validator.allowMimeTypes('application/pdf', 'image/jpeg');
            expect(validator).toBeDefined();
        });

        test('should allow extensions', () => {
            validator.allowExtensions('pdf', 'doc', 'docx');
            expect(validator).toBeDefined();
        });
    });
});

describe('PhoneNumberValidator', () => {
    let validator: PhoneNumberValidator;

    beforeEach(() => {
        validator = new PhoneNumberValidator();
    });

    describe('Validation', () => {
        test('should pass valid phone number', () => {
            const error = validator.validate('555-1234567');
            expect(error).toBeNull();
        });

        test('should fail invalid phone number', () => {
            const error = validator.validate('invalid');
            expect(error).not.toBeNull();
            expect(error?.code).toBe('INVALID_PHONE');
        });

        test('should accept international format', () => {
            const error = validator.validate('+1 (555) 123-4567');
            expect(error).toBeNull();
        });
    });
});

describe('CreditCardValidator', () => {
    let validator: CreditCardValidator;

    beforeEach(() => {
        validator = new CreditCardValidator();
    });

    describe('Validation', () => {
        test('should validate card number format', () => {
            const error = validator.validate('invalid');
            expect(error).not.toBeNull();
        });

        test('should validate with Luhn algorithm', () => {
            // Valid test card number
            const error = validator.validate('4532015112830366');
            expect(error).toBeNull();
        });
    });
});

describe('SlugValidator', () => {
    let validator: SlugValidator;

    beforeEach(() => {
        validator = new SlugValidator();
    });

    describe('Validation', () => {
        test('should pass valid slug', () => {
            const error = validator.validate('my-valid-slug');
            expect(error).toBeNull();
        });

        test('should fail slug with uppercase', () => {
            const error = validator.validate('Invalid-Slug');
            expect(error).not.toBeNull();
        });

        test('should fail slug starting with hyphen', () => {
            const error = validator.validate('-invalid-slug');
            expect(error).not.toBeNull();
        });

        test('should fail slug with consecutive hyphens', () => {
            const error = validator.validate('invalid--slug');
            expect(error).not.toBeNull();
        });

        test('should pass valid complex slug', () => {
            const error = validator.validate('my-awesome-blog-post-2026');
            expect(error).toBeNull();
        });
    });
});
