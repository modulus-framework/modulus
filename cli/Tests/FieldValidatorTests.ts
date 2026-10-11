/**
 * FieldValidator Unit Tests
 * Testing individual field validation rules
 */

import {
    FieldValidator,
    StringValidationRule,
    NumericValidationRule,
    CustomValidationRule,
    ValidationError
} from '../wwwroot/js/field-validators';

describe('FieldValidator', () => {
    describe('Text Field Validation', () => {
        test('should validate required text field', () => {
            const validator = new FieldValidator('name', 'text');
            validator.setRequired(true);

            const errors = validator.validate('');
            expect(errors.length).toBeGreaterThan(0);
            expect(errors[0].code).toBe('REQUIRED');
        });

        test('should allow empty optional field', () => {
            const validator = new FieldValidator('description', 'text');
            validator.setRequired(false);

            const errors = validator.validate('');
            expect(errors.length).toBe(0);
        });

        test('should validate minimum length', () => {
            const validator = new FieldValidator('username', 'text');
            validator.setLength(5, 20);

            const errors = validator.validate('ab');
            expect(errors.length).toBeGreaterThan(0);
            expect(errors[0].code).toBe('LENGTH_ERROR');
        });

        test('should validate maximum length', () => {
            const validator = new FieldValidator('username', 'text');
            validator.setLength(5, 20);

            const errors = validator.validate('this is a very long username that exceeds max');
            expect(errors.length).toBeGreaterThan(0);
            expect(errors[0].code).toBe('LENGTH_ERROR');
        });

        test('should validate pattern', () => {
            const validator = new FieldValidator('username', 'text');
            validator.setPattern('^[a-zA-Z0-9_]+$');

            const errors = validator.validate('user@name');
            expect(errors.length).toBeGreaterThan(0);
            expect(errors[0].code).toBe('INVALID_FORMAT');
        });

        test('should pass valid text', () => {
            const validator = new FieldValidator('username', 'text');
            validator.setRequired(true);
            validator.setLength(3, 20);

            const errors = validator.validate('validuser');
            expect(errors.length).toBe(0);
        });
    });

    describe('Email Field Validation', () => {
        test('should validate email format', () => {
            const validator = new FieldValidator('email', 'email');

            const errors = validator.validate('invalid-email');
            expect(errors.length).toBeGreaterThan(0);
            expect(errors[0].code).toBe('INVALID_FORMAT');
        });

        test('should accept valid email', () => {
            const validator = new FieldValidator('email', 'email');

            const errors = validator.validate('user@example.com');
            expect(errors.length).toBe(0);
        });

        test('should allow empty optional email', () => {
            const validator = new FieldValidator('email', 'email');
            validator.setRequired(false);

            const errors = validator.validate('');
            expect(errors.length).toBe(0);
        });

        test('should validate required email', () => {
            const validator = new FieldValidator('email', 'email');
            validator.setRequired(true);

            const errors = validator.validate('');
            expect(errors.length).toBeGreaterThan(0);
            expect(errors[0].code).toBe('REQUIRED');
        });

        test('should accept various email formats', () => {
            const validator = new FieldValidator('email', 'email');

            const validEmails = [
                'user@example.com',
                'first.last@domain.co.uk',
                'test+tag@example.org',
                'user123@sub.example.com'
            ];

            validEmails.forEach(email => {
                const errors = validator.validate(email);
                expect(errors.length).toBe(0);
            });
        });
    });

    describe('URL Field Validation', () => {
        test('should validate URL format', () => {
            const validator = new FieldValidator('website', 'url');

            const errors = validator.validate('not-a-url');
            expect(errors.length).toBeGreaterThan(0);
            expect(errors[0].code).toBe('INVALID_FORMAT');
        });

        test('should accept valid URLs', () => {
            const validator = new FieldValidator('website', 'url');

            const validUrls = [
                'https://example.com',
                'http://www.example.com',
                'https://example.com/path',
                'https://example.com:8080/path?query=value'
            ];

            validUrls.forEach(url => {
                const errors = validator.validate(url);
                expect(errors.length).toBe(0);
            });
        });

        test('should allow empty optional URL', () => {
            const validator = new FieldValidator('website', 'url');
            validator.setRequired(false);

            const errors = validator.validate('');
            expect(errors.length).toBe(0);
        });
    });

    describe('Number Field Validation', () => {
        test('should validate numeric value', () => {
            const validator = new FieldValidator('quantity', 'number');

            const errors = validator.validate('not-a-number');
            expect(errors.length).toBeGreaterThan(0);
            expect(errors[0].code).toBe('INVALID_FORMAT');
        });

        test('should validate minimum value', () => {
            const validator = new FieldValidator('age', 'number');
            validator.setRange(18, 100);

            const errors = validator.validate('10');
            expect(errors.length).toBeGreaterThan(0);
            expect(errors[0].code).toBe('RANGE_ERROR');
        });

        test('should validate maximum value', () => {
            const validator = new FieldValidator('age', 'number');
            validator.setRange(18, 100);

            const errors = validator.validate('150');
            expect(errors.length).toBeGreaterThan(0);
            expect(errors[0].code).toBe('RANGE_ERROR');
        });

        test('should accept valid number within range', () => {
            const validator = new FieldValidator('age', 'number');
            validator.setRange(18, 100);

            const errors = validator.validate('25');
            expect(errors.length).toBe(0);
        });

        test('should accept decimal numbers', () => {
            const validator = new FieldValidator('price', 'decimal');

            const errors = validator.validate('19.99');
            expect(errors.length).toBe(0);
        });

        test('should accept negative numbers', () => {
            const validator = new FieldValidator('temperature', 'number');

            const errors = validator.validate('-5');
            expect(errors.length).toBe(0);
        });
    });

    describe('Custom Validation Rules', () => {
        test('should add and validate custom rule', () => {
            const validator = new FieldValidator('username', 'text');
            const customRule = new CustomValidationRule(
                (value) => value.length >= 3,
                'Username must be at least 3 characters',
                'CUSTOM_MIN_LENGTH'
            );

            validator.addRule(customRule);
            const errors = validator.validate('ab');
            expect(errors.length).toBeGreaterThan(0);
            expect(errors[0].code).toBe('CUSTOM_MIN_LENGTH');
        });

        test('should chain multiple rules', () => {
            const validator = new FieldValidator('password', 'text');
            validator.setRequired(true);
            validator.setLength(8, 50);

            const rule1 = new CustomValidationRule(
                (value) => /[A-Z]/.test(value),
                'Must contain uppercase letter',
                'NO_UPPERCASE'
            );
            const rule2 = new CustomValidationRule(
                (value) => /[0-9]/.test(value),
                'Must contain number',
                'NO_NUMBER'
            );

            validator.addRule(rule1).addRule(rule2);

            const errors = validator.validate('password');
            expect(errors.length).toBeGreaterThan(0);
        });

        test('should pass custom validation', () => {
            const validator = new FieldValidator('username', 'text');
            const rule = new CustomValidationRule(
                (value) => !value.includes('admin'),
                'Username cannot be admin',
                'RESERVED_NAME'
            );

            validator.addRule(rule);
            const errors = validator.validate('john_user');
            expect(errors.length).toBe(0);
        });
    });

    describe('Chaining Methods', () => {
        test('should chain setRequired', () => {
            const validator = new FieldValidator('name', 'text')
                .setRequired(true)
                .setLength(3, 50);

            const errors = validator.validate('');
            expect(errors.length).toBeGreaterThan(0);
        });

        test('should chain setLength', () => {
            const validator = new FieldValidator('name', 'text')
                .setLength(3, 50);

            const errors1 = validator.validate('ab');
            const errors2 = validator.validate('valid name');

            expect(errors1.length).toBeGreaterThan(0);
            expect(errors2.length).toBe(0);
        });

        test('should chain setRange', () => {
            const validator = new FieldValidator('age', 'number')
                .setRange(18, 100);

            const errors = validator.validate('25');
            expect(errors.length).toBe(0);
        });

        test('should chain setPattern', () => {
            const validator = new FieldValidator('code', 'text')
                .setPattern('^[A-Z]{3}[0-9]{3}$');

            const errors1 = validator.validate('ABC123');
            const errors2 = validator.validate('abc123');

            expect(errors1.length).toBe(0);
            expect(errors2.length).toBeGreaterThan(0);
        });
    });

    describe('Validation Error Details', () => {
        test('should include error details', () => {
            const validator = new FieldValidator('email', 'email');
            const errors = validator.validate('invalid');

            expect(errors[0]).toHaveProperty('field');
            expect(errors[0]).toHaveProperty('message');
            expect(errors[0]).toHaveProperty('code');
            expect(errors[0]).toHaveProperty('severity');
        });

        test('should set correct severity', () => {
            const validator = new FieldValidator('name', 'text');
            validator.setRequired(true);

            const errors = validator.validate('');
            expect(errors[0].severity).toBe('error');
        });

        test('should include field name in error', () => {
            const validator = new FieldValidator('email', 'email');
            const errors = validator.validate('invalid');

            expect(errors[0].field).toBe('email');
        });
    });

    describe('Special Cases', () => {
        test('should handle null values', () => {
            const validator = new FieldValidator('optional', 'text');
            validator.setRequired(false);

            const errors = validator.validate('');
            expect(errors.length).toBe(0);
        });

        test('should handle whitespace', () => {
            const validator = new FieldValidator('name', 'text');
            validator.setRequired(true);

            const errors = validator.validate('   ');
            expect(errors.length).toBeGreaterThan(0);
        });

        test('should handle special characters in text', () => {
            const validator = new FieldValidator('name', 'text');

            const errors = validator.validate('John O\'Brien-Smith');
            expect(errors.length).toBe(0);
        });

        test('should handle leading/trailing spaces', () => {
            const validator = new FieldValidator('name', 'text');
            validator.setLength(3, 20);

            const errors = validator.validate('  test  ');
            // Should trim and validate
            expect(errors.length).toBe(0);
        });
    });
});

describe('ValidationRules', () => {
    describe('StringValidationRule', () => {
        test('should validate string length', () => {
            const rule = new StringValidationRule(3, 10);
            const error = rule.validate('ab');

            expect(error).not.toBeNull();
            expect(error?.code).toBe('LENGTH_ERROR');
        });

        test('should validate pattern', () => {
            const rule = new StringValidationRule(undefined, undefined, '^[a-z]+$');
            const error = rule.validate('ABC123');

            expect(error).not.toBeNull();
            expect(error?.code).toBe('INVALID_FORMAT');
        });
    });

    describe('NumericValidationRule', () => {
        test('should validate number range', () => {
            const rule = new NumericValidationRule(10, 100);
            const error = rule.validate('5');

            expect(error).not.toBeNull();
            expect(error?.code).toBe('RANGE_ERROR');
        });

        test('should validate number format', () => {
            const rule = new NumericValidationRule();
            const error = rule.validate('not-a-number');

            expect(error).not.toBeNull();
            expect(error?.code).toBe('INVALID_FORMAT');
        });
    });

    describe('CustomValidationRule', () => {
        test('should execute custom validation', () => {
            const rule = new CustomValidationRule(
                (value) => value.length > 5,
                'Too short',
                'TOO_SHORT'
            );

            const error = rule.validate('abc');
            expect(error).not.toBeNull();
            expect(error?.message).toBe('Too short');
        });

        test('should pass custom validation', () => {
            const rule = new CustomValidationRule(
                (value) => value.length > 5,
                'Too short'
            );

            const error = rule.validate('long-value');
            expect(error).toBeNull();
        });
    });
});
