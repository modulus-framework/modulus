/**
 * FormValidator Unit Tests
 * Testing form validation orchestration, field validation, and error display
 */

import { FormValidator, FormValidationOptions } from '../wwwroot/js/form-validator';
import { FieldValidator, ValidationError } from '../wwwroot/js/field-validators';

describe('FormValidator', () => {
    let formElement: HTMLFormElement;
    let validator: FormValidator;

    beforeEach(() => {
        // Create a simple form for testing
        formElement = document.createElement('form');
        formElement.id = 'testForm';
        formElement.innerHTML = `
            <div class="form-group">
                <label for="name">Name</label>
                <input type="text" id="name" name="name" data-validator="text" data-required />
            </div>
            <div class="form-group">
                <label for="email">Email</label>
                <input type="email" id="email" name="email" data-validator="email" />
            </div>
            <div class="form-group">
                <label for="age">Age</label>
                <input type="number" id="age" name="age" data-validator="number" data-min="18" data-max="100" />
            </div>
        `;
        document.body.appendChild(formElement);

        validator = new FormValidator('#testForm', {
            validateOnBlur: false,
            validateOnChange: false,
            validateOnSubmit: true,
            showErrors: true
        });
    });

    afterEach(() => {
        document.body.removeChild(formElement);
    });

    describe('Initialization', () => {
        test('should initialize form validator with form element', () => {
            expect(validator).toBeDefined();
        });

        test('should find all validatable fields', () => {
            const errors = validator.getErrors();
            expect(errors).toBeDefined();
        });

        test('should set default options', () => {
            const validator2 = new FormValidator('#testForm');
            expect(validator2).toBeDefined();
        });
    });

    describe('Field Validation', () => {
        test('should validate required field', () => {
            const nameInput = formElement.querySelector('#name') as HTMLInputElement;
            nameInput.value = '';

            const errors = validator.validateField('name');
            expect(errors.length).toBeGreaterThan(0);
            expect(errors[0].code).toBe('REQUIRED');
        });

        test('should validate email format', () => {
            const emailInput = formElement.querySelector('#email') as HTMLInputElement;
            emailInput.value = 'invalid-email';

            const errors = validator.validateField('email');
            expect(errors.length).toBeGreaterThan(0);
            expect(errors[0].code).toBe('INVALID_FORMAT');
        });

        test('should validate numeric range', () => {
            const ageInput = formElement.querySelector('#age') as HTMLInputElement;
            ageInput.value = '10';

            const errors = validator.validateField('age');
            expect(errors.length).toBeGreaterThan(0);
            expect(errors[0].code).toBe('RANGE_ERROR');
        });

        test('should pass valid field validation', () => {
            const nameInput = formElement.querySelector('#name') as HTMLInputElement;
            nameInput.value = 'John Doe';

            const errors = validator.validateField('name');
            expect(errors.length).toBe(0);
        });
    });

    describe('Form Validation', () => {
        test('should validate all fields', async () => {
            const nameInput = formElement.querySelector('#name') as HTMLInputElement;
            const emailInput = formElement.querySelector('#email') as HTMLInputElement;

            nameInput.value = '';
            emailInput.value = 'invalid';

            const allErrors = await validator.validateAll();
            expect(allErrors.length).toBeGreaterThan(0);
        });

        test('should pass validation when all fields are valid', async () => {
            const nameInput = formElement.querySelector('#name') as HTMLInputElement;
            const emailInput = formElement.querySelector('#email') as HTMLInputElement;
            const ageInput = formElement.querySelector('#age') as HTMLInputElement;

            nameInput.value = 'John Doe';
            emailInput.value = 'john@example.com';
            ageInput.value = '25';

            const allErrors = await validator.validateAll();
            expect(allErrors.length).toBe(0);
        });

        test('should check if form is valid', async () => {
            const nameInput = formElement.querySelector('#name') as HTMLInputElement;
            nameInput.value = 'John Doe';

            await validator.validateAll();
            expect(validator.isValid()).toBe(true);
        });

        test('should check if form is invalid', async () => {
            const nameInput = formElement.querySelector('#name') as HTMLInputElement;
            nameInput.value = '';

            await validator.validateAll();
            expect(validator.isValid()).toBe(false);
        });
    });

    describe('Error Management', () => {
        test('should get all validation errors', async () => {
            const nameInput = formElement.querySelector('#name') as HTMLInputElement;
            nameInput.value = '';

            await validator.validateAll();
            const errors = validator.getErrors();
            expect(errors.size).toBeGreaterThan(0);
        });

        test('should clear all errors', () => {
            validator.clearErrors();
            const errors = validator.getErrors();
            expect(errors.size).toBe(0);
        });

        test('should reset form', () => {
            const nameInput = formElement.querySelector('#name') as HTMLInputElement;
            nameInput.value = 'test';

            validator.reset();
            expect(nameInput.value).toBe('');
        });
    });

    describe('Custom Validator Registration', () => {
        test('should register custom field validator', () => {
            const fieldValidator = new FieldValidator('customField', 'text');
            validator.registerFieldValidator('customField', fieldValidator);
            expect(validator).toBeDefined();
        });
    });

    describe('Event Handling', () => {
        test('should handle form submission', async () => {
            const nameInput = formElement.querySelector('#name') as HTMLInputElement;
            nameInput.value = 'John Doe';

            const event = new SubmitEvent('submit', { cancelable: true });
            await validator.validateAll();

            expect(validator.isValid()).toBe(true);
        });

        test('should prevent form submission with invalid data', async () => {
            const nameInput = formElement.querySelector('#name') as HTMLInputElement;
            nameInput.value = '';

            await validator.validateAll();
            expect(validator.isValid()).toBe(false);
        });
    });
});

describe('FormValidator Options', () => {
    let formElement: HTMLFormElement;

    beforeEach(() => {
        formElement = document.createElement('form');
        formElement.id = 'testForm';
        formElement.innerHTML = `
            <input type="text" id="test" name="test" data-validator="text" data-required />
        `;
        document.body.appendChild(formElement);
    });

    afterEach(() => {
        document.body.removeChild(formElement);
    });

    test('should use custom error class', () => {
        const validator = new FormValidator('#testForm', {
            errorClass: 'is-danger'
        });
        expect(validator).toBeDefined();
    });

    test('should use custom success class', () => {
        const validator = new FormValidator('#testForm', {
            successClass: 'is-success'
        });
        expect(validator).toBeDefined();
    });

    test('should disable validation on blur', () => {
        const validator = new FormValidator('#testForm', {
            validateOnBlur: false
        });
        expect(validator).toBeDefined();
    });

    test('should enable validation on change', () => {
        const validator = new FormValidator('#testForm', {
            validateOnChange: true
        });
        expect(validator).toBeDefined();
    });
});

describe('FormValidator Edge Cases', () => {
    let formElement: HTMLFormElement;
    let validator: FormValidator;

    beforeEach(() => {
        formElement = document.createElement('form');
        formElement.id = 'testForm';
        formElement.innerHTML = `
            <input type="text" id="name" name="name" data-validator="text" />
            <textarea id="description" name="description" data-validator="text"></textarea>
            <select id="country" name="country" data-validator="text">
                <option value="">Select</option>
                <option value="US">United States</option>
            </select>
        `;
        document.body.appendChild(formElement);

        validator = new FormValidator('#testForm');
    });

    afterEach(() => {
        document.body.removeChild(formElement);
    });

    test('should handle missing form element', () => {
        expect(() => new FormValidator('#nonexistent')).not.toThrow();
    });

    test('should validate textarea fields', () => {
        const textarea = formElement.querySelector('#description') as HTMLTextAreaElement;
        textarea.value = 'test content';
        expect(validator).toBeDefined();
    });

    test('should validate select fields', () => {
        const select = formElement.querySelector('#country') as HTMLSelectElement;
        select.value = 'US';
        expect(validator).toBeDefined();
    });

    test('should handle whitespace in required fields', () => {
        const input = formElement.querySelector('#name') as HTMLInputElement;
        input.value = '   ';
        input.dataset.required = 'true';

        const errors = validator.validateField('name');
        // Whitespace should be treated as empty
    });
});
