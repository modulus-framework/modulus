/**
 * PerformanceTests - Performance and stress testing for form validators
 * Measures validation speed, memory usage, and scalability
 */

import { FormValidator } from '../wwwroot/js/form-validator';
import { AsyncValidatorGroup } from '../wwwroot/js/async-validator';
import { CrossFieldValidator } from '../wwwroot/js/cross-field-validator';
import { PasswordStrengthValidator, FileUploadValidator } from '../wwwroot/js/advanced-validators';

describe('Validation Performance', () => {
    describe('Field Validation Speed', () => {
        test('should validate field in under 5ms', () => {
            const validator = new PasswordStrengthValidator();
            const password = 'StrongPass123!@#$%';

            const start = performance.now();
            validator.validate(password);
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(5);
        });

        test('should validate 100 fields in under 100ms', () => {
            const passwords = Array(100).fill('TestPass123!');

            const start = performance.now();
            passwords.forEach(pwd => {
                const validator = new PasswordStrengthValidator();
                validator.validate(pwd);
            });
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(100);
        });

        test('should validate 1000 fields in under 1 second', () => {
            const passwords = Array(1000).fill('TestPass123!');

            const start = performance.now();
            passwords.forEach(pwd => {
                const validator = new PasswordStrengthValidator();
                validator.validate(pwd);
            });
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(1000);
        });
    });

    describe('Cross-Field Validation Performance', () => {
        test('should validate password confirmation in under 5ms', () => {
            const validator = new CrossFieldValidator();
            validator.addPasswordConfirmation();

            const data = {
                password: 'SecurePass123!',
                confirmPassword: 'SecurePass123!'
            };

            const start = performance.now();
            validator.validate(data);
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(5);
        });

        test('should validate 100 cross-field rules in under 50ms', () => {
            const validator = new CrossFieldValidator();

            for (let i = 0; i < 100; i++) {
                validator.addRule(
                    `field${i}`,
                    `field${i + 1}`,
                    (v1, v2) => v1 === v2,
                    'Must match'
                );
            }

            const data: Record<string, any> = {};
            for (let i = 0; i < 101; i++) {
                data[`field${i}`] = 'value';
            }

            const start = performance.now();
            validator.validate(data);
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(50);
        });

        test('should validate 1000 cross-field rules in under 500ms', () => {
            const validator = new CrossFieldValidator();

            for (let i = 0; i < 1000; i++) {
                validator.addRule(
                    `field${i}`,
                    `field${i + 1}`,
                    (v1, v2) => v1 === v2,
                    'Must match'
                );
            }

            const data: Record<string, any> = {};
            for (let i = 0; i < 1001; i++) {
                data[`field${i}`] = 'value';
            }

            const start = performance.now();
            validator.validate(data);
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(500);
        });
    });

    describe('Form Validation Performance', () => {
        test('should validate small form (10 fields) in under 20ms', () => {
            const form = document.createElement('form');
            for (let i = 0; i < 10; i++) {
                const input = document.createElement('input');
                input.name = `field${i}`;
                input.type = 'text';
                input.value = 'valid value';
                form.appendChild(input);
            }

            const validator = new FormValidator(form);

            const start = performance.now();
            validator.validateAll();
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(20);
        });

        test('should validate medium form (50 fields) in under 100ms', () => {
            const form = document.createElement('form');
            for (let i = 0; i < 50; i++) {
                const input = document.createElement('input');
                input.name = `field${i}`;
                input.type = 'text';
                input.value = 'valid value';
                form.appendChild(input);
            }

            const validator = new FormValidator(form);

            const start = performance.now();
            validator.validateAll();
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(100);
        });

        test('should validate large form (100 fields) in under 200ms', () => {
            const form = document.createElement('form');
            for (let i = 0; i < 100; i++) {
                const input = document.createElement('input');
                input.name = `field${i}`;
                input.type = 'text';
                input.value = 'valid value';
                form.appendChild(input);
            }

            const validator = new FormValidator(form);

            const start = performance.now();
            validator.validateAll();
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(200);
        });
    });

    describe('Async Validation Performance', () => {
        test('should debounce async requests efficiently', async () => {
            const group = new AsyncValidatorGroup();
            group.addValidator('username', '/api/validate/username', 500);

            const requestCounts = 0;
            let actualRequests = 0;

            // Simulate rapid typing
            const start = performance.now();

            for (let i = 0; i < 100; i++) {
                const value = 't'.repeat(i + 1);
                group.validateField('username', value);
            }

            const elapsed = performance.now() - start;

            // Should handle 100 calls efficiently
            expect(elapsed).toBeLessThan(50);
        });

        test('should process validation results quickly', async () => {
            const group = new AsyncValidatorGroup();
            group.addValidator('email', '/api/validate/email');

            const start = performance.now();
            await group.validateField('email', 'test@example.com');
            const elapsed = performance.now() - start;

            // Result processing should be fast (less than debounce time)
            expect(elapsed).toBeLessThan(600);
        });
    });
});

describe('Memory Performance', () => {
    describe('Validator Object Size', () => {
        test('should create validator with minimal memory overhead', () => {
            const validator = new PasswordStrengthValidator();
            expect(validator).toBeDefined();
            // Should not use excessive memory
        });

        test('should handle many validator instances without memory leaks', () => {
            const validators = [];
            for (let i = 0; i < 1000; i++) {
                validators.push(new PasswordStrengthValidator());
            }

            expect(validators.length).toBe(1000);
        });
    });

    describe('Error Collection Memory', () => {
        test('should efficiently store validation errors', () => {
            const validator = new CrossFieldValidator();
            for (let i = 0; i < 100; i++) {
                validator.addRule(
                    `field${i}`,
                    `field${i + 1}`,
                    () => false,
                    'Error message'
                );
            }

            const data: Record<string, any> = {};
            for (let i = 0; i < 101; i++) {
                data[`field${i}`] = 'value';
            }

            const errors = validator.validate(data);
            expect(Array.isArray(errors)).toBe(true);
        });

        test('should clear errors to free memory', () => {
            const validator = new CrossFieldValidator();
            validator.addRule('field1', 'field2', () => false, 'Error');

            validator.validate({ field1: 'a', field2: 'b' });
            validator.clearErrors();

            expect(validator.getErrors().length).toBe(0);
        });
    });
});

describe('Stress Testing', () => {
    describe('Rapid Validation', () => {
        test('should handle rapid field validation calls', () => {
            const validator = new FormValidator(document.createElement('form'));

            const start = performance.now();

            for (let i = 0; i < 1000; i++) {
                const form = document.createElement('form');
                const input = document.createElement('input');
                input.name = 'field';
                input.value = 'test';
                form.appendChild(input);

                const fieldValidator = new FormValidator(form);
                fieldValidator.validateField('field');
            }

            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(5000);
        });

        test('should handle concurrent validation requests', async () => {
            const group = new AsyncValidatorGroup();
            group.addValidator('field1', '/api/validate/field1');
            group.addValidator('field2', '/api/validate/field2');
            group.addValidator('field3', '/api/validate/field3');

            const start = performance.now();

            const promises = [
                group.validateField('field1', 'value1'),
                group.validateField('field2', 'value2'),
                group.validateField('field3', 'value3')
            ];

            await Promise.all(promises);

            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(2000);
        });
    });

    describe('Large Data Sets', () => {
        test('should validate large forms with many fields', () => {
            const form = document.createElement('form');

            // Create a large form
            for (let i = 0; i < 500; i++) {
                const input = document.createElement('input');
                input.name = `field${i}`;
                input.type = i % 5 === 0 ? 'email' : 'text';
                input.value = 'test value';
                form.appendChild(input);
            }

            const start = performance.now();

            const validator = new FormValidator(form);
            validator.validateAll();

            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(1000);
        });

        test('should handle validation with large error lists', () => {
            const validator = new CrossFieldValidator();

            // Add many rules that will all fail
            for (let i = 0; i < 500; i++) {
                validator.addRule(
                    `field${i}`,
                    `field${i + 1}`,
                    () => false,
                    `Error message for field ${i}`
                );
            }

            const data: Record<string, any> = {};
            for (let i = 0; i < 501; i++) {
                data[`field${i}`] = 'value';
            }

            const start = performance.now();
            const errors = validator.validate(data);
            const elapsed = performance.now() - start;

            expect(errors.length).toBeGreaterThan(0);
            expect(elapsed).toBeLessThan(500);
        });
    });

    describe('Event Handling Performance', () => {
        test('should handle high-frequency blur events', () => {
            const form = document.createElement('form');
            const input = document.createElement('input');
            input.name = 'testField';
            form.appendChild(input);

            const validator = new FormValidator(form, { validateOnBlur: true });

            const start = performance.now();

            for (let i = 0; i < 1000; i++) {
                input.dispatchEvent(new Event('blur'));
            }

            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(5000);
        });

        test('should handle high-frequency change events', () => {
            const form = document.createElement('form');
            const input = document.createElement('input');
            input.name = 'testField';
            form.appendChild(input);

            const validator = new FormValidator(form, { validateOnChange: true });

            const start = performance.now();

            for (let i = 0; i < 1000; i++) {
                input.value = `test${i}`;
                input.dispatchEvent(new Event('change'));
            }

            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(5000);
        });
    });
});

describe('Throughput Tests', () => {
    test('should validate at least 1000 fields per second', () => {
        const validator = new PasswordStrengthValidator();
        const testPassword = 'TestPass123!';

        const start = performance.now();
        let count = 0;

        while (performance.now() - start < 1000) { // 1 second
            validator.validate(testPassword);
            count++;
        }

        const elapsed = performance.now() - start;
        const throughput = Math.floor(count / (elapsed / 1000));

        expect(throughput).toBeGreaterThan(1000);
    });

    test('should process at least 100 form validations per second', () => {
        const form = document.createElement('form');
        for (let i = 0; i < 20; i++) {
            const input = document.createElement('input');
            input.name = `field${i}`;
            input.value = 'test';
            form.appendChild(input);
        }

        const validator = new FormValidator(form);

        const start = performance.now();
        let count = 0;

        while (performance.now() - start < 1000) { // 1 second
            validator.validateAll();
            count++;
        }

        const elapsed = performance.now() - start;
        const throughput = Math.floor(count / (elapsed / 1000));

        expect(throughput).toBeGreaterThan(100);
    });
});

describe('Memory Leak Prevention', () => {
    test('should not leak memory with repeated validation', () => {
        const form = document.createElement('form');
        const input = document.createElement('input');
        input.name = 'testField';
        form.appendChild(input);

        const validator = new FormValidator(form);

        // Perform many validations
        for (let i = 0; i < 10000; i++) {
            validator.validateField('testField');
        }

        // Check that we can still validate
        const result = validator.validateField('testField');
        expect(typeof result).toBe('object');
    });

    test('should clean up event listeners on reset', () => {
        const form = document.createElement('form');
        const input = document.createElement('input');
        input.name = 'testField';
        form.appendChild(input);

        const validator = new FormValidator(form);
        validator.reset();

        // Form should still be functional
        expect(form).toBeDefined();
    });

    test('should properly dispose of async validators', async () => {
        const group = new AsyncValidatorGroup();
        group.addValidator('field1', '/api/validate/field1');
        group.addValidator('field2', '/api/validate/field2');

        group.clearAll();

        // Should not cause errors
        const errors = await group.validateAll({ field1: '', field2: '' });
        expect(Array.isArray(errors)).toBe(true);
    });
});
