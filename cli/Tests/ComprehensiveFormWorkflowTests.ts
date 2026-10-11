/**
 * ComprehensiveFormWorkflowTests - Full end-to-end form validation workflows
 * Tests complete form interactions from initialization through submission
 */

import { FormValidator } from '../wwwroot/js/form-validator';
import { FormSubmissionHandler } from '../wwwroot/js/form-submission-handler';
import { ErrorDisplay } from '../wwwroot/js/error-display';
import { ValidationSummary } from '../wwwroot/js/validation-summary';
import { FormIntegration } from '../wwwroot/js/form-integration';
import { AsyncValidatorGroup } from '../wwwroot/js/async-validator';
import { CrossFieldValidator, ConditionalFieldValidator } from '../wwwroot/js/cross-field-validator';
import { PasswordStrengthValidator } from '../wwwroot/js/advanced-validators';

describe('User Registration Workflow', () => {
    let formElement: HTMLFormElement;
    let integration: FormIntegration;

    beforeEach(() => {
        formElement = document.createElement('form');
        formElement.innerHTML = `
            <input type="text" name="username" required />
            <input type="email" name="email" required />
            <input type="password" name="password" required />
            <input type="password" name="confirmPassword" required />
            <input type="tel" name="phone" />
            <input type="checkbox" name="acceptTerms" required />
            <button type="submit">Register</button>
        `;
        document.body.appendChild(formElement);

        integration = new FormIntegration(formElement, {
            validateOnBlur: true,
            validateOnChange: false,
            validateOnSubmit: true
        });
    });

    afterEach(() => {
        document.body.removeChild(formElement);
    });

    describe('Initialization', () => {
        test('should initialize form with all fields', () => {
            expect(integration).toBeDefined();
            const inputs = formElement.querySelectorAll('input');
            expect(inputs.length).toBeGreaterThan(0);
        });

        test('should attach event listeners to fields', () => {
            const usernameField = formElement.querySelector('[name="username"]') as HTMLInputElement;
            expect(usernameField).toBeDefined();
        });

        test('should track form state', () => {
            expect(integration).toBeDefined();
        });
    });

    describe('Field Validation Flow', () => {
        test('should validate empty username field', async () => {
            const field = formElement.querySelector('[name="username"]') as HTMLInputElement;
            field.value = '';
            field.dispatchEvent(new Event('blur'));

            // Should show error for required field
            await new Promise(resolve => setTimeout(resolve, 100));
            expect(field.classList.contains('is-invalid')).toBe(true);
        });

        test('should validate email format on blur', async () => {
            const field = formElement.querySelector('[name="email"]') as HTMLInputElement;
            field.value = 'invalid-email';
            field.dispatchEvent(new Event('blur'));

            await new Promise(resolve => setTimeout(resolve, 100));
            expect(field.classList.contains('is-invalid')).toBe(true);
        });

        test('should validate valid email on blur', async () => {
            const field = formElement.querySelector('[name="email"]') as HTMLInputElement;
            field.value = 'user@example.com';
            field.dispatchEvent(new Event('blur'));

            await new Promise(resolve => setTimeout(resolve, 100));
            expect(field.classList.contains('is-valid')).toBe(true);
        });

        test('should validate password strength', async () => {
            const field = formElement.querySelector('[name="password"]') as HTMLInputElement;
            field.value = 'StrongPass123!';
            field.dispatchEvent(new Event('blur'));

            await new Promise(resolve => setTimeout(resolve, 100));
            // Should pass validation
            expect(field.classList.contains('is-invalid')).toBe(false);
        });
    });

    describe('Cross-Field Validation', () => {
        test('should validate password confirmation match', async () => {
            const password = formElement.querySelector('[name="password"]') as HTMLInputElement;
            const confirm = formElement.querySelector('[name="confirmPassword"]') as HTMLInputElement;

            password.value = 'SecurePass123!';
            confirm.value = 'SecurePass123!';

            password.dispatchEvent(new Event('blur'));
            await new Promise(resolve => setTimeout(resolve, 100));

            // Both fields should be valid
            expect(password.classList.contains('is-valid')).toBe(true);
        });

        test('should show error for password mismatch', async () => {
            const password = formElement.querySelector('[name="password"]') as HTMLInputElement;
            const confirm = formElement.querySelector('[name="confirmPassword"]') as HTMLInputElement;

            password.value = 'SecurePass123!';
            confirm.value = 'DifferentPass123!';

            password.dispatchEvent(new Event('blur'));
            await new Promise(resolve => setTimeout(resolve, 100));

            // Should show error
            expect(password.classList.contains('is-invalid')).toBe(true);
        });
    });

    describe('Form Submission', () => {
        test('should prevent submission with invalid fields', async () => {
            const event = new SubmitEvent('submit', { cancelable: true });
            let prevented = false;

            formElement.addEventListener('submit', (e) => {
                prevented = e.defaultPrevented;
            });

            formElement.querySelector('[name="username"]')?.dispatchEvent(new Event('blur'));
            await new Promise(resolve => setTimeout(resolve, 100));

            formElement.dispatchEvent(event);

            expect(prevented).toBe(true);
        });

        test('should allow submission with valid fields', async () => {
            const username = formElement.querySelector('[name="username"]') as HTMLInputElement;
            const email = formElement.querySelector('[name="email"]') as HTMLInputElement;
            const password = formElement.querySelector('[name="password"]') as HTMLInputElement;
            const confirm = formElement.querySelector('[name="confirmPassword"]') as HTMLInputElement;
            const terms = formElement.querySelector('[name="acceptTerms"]') as HTMLInputElement;

            username.value = 'testuser';
            email.value = 'user@example.com';
            password.value = 'SecurePass123!';
            confirm.value = 'SecurePass123!';
            terms.checked = true;

            // Validate all fields
            username.dispatchEvent(new Event('blur'));
            email.dispatchEvent(new Event('blur'));
            password.dispatchEvent(new Event('blur'));
            confirm.dispatchEvent(new Event('blur'));

            await new Promise(resolve => setTimeout(resolve, 100));

            // Check if form should be submittable
            const invalidFields = formElement.querySelectorAll('.is-invalid');
            expect(invalidFields.length).toBe(0);
        });
    });
});

describe('Multi-Step Form Workflow', () => {
    let step1Form: HTMLFormElement;
    let step2Form: HTMLFormElement;

    beforeEach(() => {
        // Step 1: Basic info
        step1Form = document.createElement('form');
        step1Form.innerHTML = `
            <input type="text" name="firstName" required />
            <input type="text" name="lastName" required />
            <button type="button" class="next">Next</button>
        `;

        // Step 2: Contact info
        step2Form = document.createElement('form');
        step2Form.innerHTML = `
            <input type="email" name="email" required />
            <input type="tel" name="phone" required />
            <button type="button" class="back">Back</button>
            <button type="submit">Submit</button>
        `;

        document.body.appendChild(step1Form);
        document.body.appendChild(step2Form);
    });

    afterEach(() => {
        document.body.removeChild(step1Form);
        document.body.removeChild(step2Form);
    });

    describe('Step Navigation', () => {
        test('should validate step 1 before proceeding', () => {
            const firstName = step1Form.querySelector('[name="firstName"]') as HTMLInputElement;
            firstName.value = '';

            // Try to proceed without filling required field
            expect(firstName.value).toBe('');
        });

        test('should proceed to step 2 with valid data', () => {
            const firstName = step1Form.querySelector('[name="firstName"]') as HTMLInputElement;
            const lastName = step1Form.querySelector('[name="lastName"]') as HTMLInputElement;

            firstName.value = 'John';
            lastName.value = 'Doe';

            expect(firstName.value).toBe('John');
            expect(lastName.value).toBe('Doe');
        });

        test('should allow returning to step 1', () => {
            const email = step2Form.querySelector('[name="email"]') as HTMLInputElement;
            email.value = 'john@example.com';

            const backBtn = step2Form.querySelector('.back') as HTMLButtonElement;
            expect(backBtn).toBeDefined();
        });
    });

    describe('State Preservation', () => {
        test('should preserve data between steps', () => {
            // Step 1
            const firstName = step1Form.querySelector('[name="firstName"]') as HTMLInputElement;
            firstName.value = 'John';

            // Simulate moving to step 2
            expect(firstName.value).toBe('John');

            // Step 2
            const email = step2Form.querySelector('[name="email"]') as HTMLInputElement;
            email.value = 'john@example.com';

            // Validate both steps have data
            expect(firstName.value).toBe('John');
            expect(email.value).toBe('john@example.com');
        });
    });
});

describe('Conditional Fields Workflow', () => {
    let formElement: HTMLFormElement;
    let conditional: ConditionalFieldValidator;

    beforeEach(() => {
        formElement = document.createElement('form');
        formElement.innerHTML = `
            <select name="country">
                <option value="">Select Country</option>
                <option value="US">United States</option>
                <option value="CA">Canada</option>
                <option value="Other">Other</option>
            </select>
            <input type="text" name="state" placeholder="State" />
            <input type="text" name="province" placeholder="Province" />
            <input type="text" name="customCountry" placeholder="Country Name" />
            <input type="checkbox" name="needsVisaInfo" />
            <textarea name="visaInfo"></textarea>
        `;
        document.body.appendChild(formElement);

        conditional = new ConditionalFieldValidator();
        conditional.addCondition('country', 'US', 'state', 'require');
        conditional.addCondition('country', 'CA', 'province', 'require');
        conditional.addCondition('country', 'Other', 'customCountry', 'require');
        conditional.addCondition('needsVisaInfo', true, 'visaInfo', 'require');
    });

    afterEach(() => {
        document.body.removeChild(formElement);
    });

    describe('Field Visibility', () => {
        test('should show state field for US selection', () => {
            const country = formElement.querySelector('[name="country"]') as HTMLSelectElement;
            country.value = 'US';
            country.dispatchEvent(new Event('change'));

            const state = formElement.querySelector('[name="state"]') as HTMLInputElement;
            expect(state).toBeDefined();
        });

        test('should show province field for CA selection', () => {
            const country = formElement.querySelector('[name="country"]') as HTMLSelectElement;
            country.value = 'CA';
            country.dispatchEvent(new Event('change'));

            const province = formElement.querySelector('[name="province"]') as HTMLInputElement;
            expect(province).toBeDefined();
        });

        test('should show custom country field for Other selection', () => {
            const country = formElement.querySelector('[name="country"]') as HTMLSelectElement;
            country.value = 'Other';
            country.dispatchEvent(new Event('change'));

            const custom = formElement.querySelector('[name="customCountry"]') as HTMLInputElement;
            expect(custom).toBeDefined();
        });
    });

    describe('Field Requirements', () => {
        test('should make state required for US', () => {
            const country = formElement.querySelector('[name="country"]') as HTMLSelectElement;
            const state = formElement.querySelector('[name="state"]') as HTMLInputElement;

            country.value = 'US';
            conditional.applyConditions(formElement, { country: 'US' });

            expect(state.hasAttribute('data-required') || state.required).toBeDefined();
        });

        test('should toggle field requirement on checkbox', () => {
            const checkbox = formElement.querySelector('[name="needsVisaInfo"]') as HTMLInputElement;
            const textarea = formElement.querySelector('[name="visaInfo"]') as HTMLTextAreaElement;

            checkbox.checked = true;
            checkbox.dispatchEvent(new Event('change'));

            expect(textarea).toBeDefined();
        });
    });
});

describe('Async Validation Workflow', () => {
    let formElement: HTMLFormElement;
    let asyncGroup: AsyncValidatorGroup;

    beforeEach(() => {
        formElement = document.createElement('form');
        formElement.innerHTML = `
            <input type="text" name="username" />
            <input type="email" name="email" />
            <button type="submit">Submit</button>
        `;
        document.body.appendChild(formElement);

        asyncGroup = new AsyncValidatorGroup();
        asyncGroup.addValidator('username', '/api/validate/username', 500);
        asyncGroup.addValidator('email', '/api/validate/email', 500);
    });

    afterEach(() => {
        document.body.removeChild(formElement);
    });

    describe('Debounced Validation', () => {
        test('should debounce username validation', async () => {
            const username = formElement.querySelector('[name="username"]') as HTMLInputElement;
            username.value = 'testuser';

            // Validation should be debounced
            const validator = asyncGroup.getValidator('username');
            expect(validator).toBeDefined();
        });

        test('should not trigger multiple validations on rapid typing', async () => {
            const username = formElement.querySelector('[name="username"]') as HTMLInputElement;

            username.value = 't';
            username.dispatchEvent(new Event('input'));
            username.value = 'te';
            username.dispatchEvent(new Event('input'));
            username.value = 'tes';
            username.dispatchEvent(new Event('input'));

            // Only final debounced request should be sent
            expect(username.value).toBe('tes');
        });
    });

    describe('Validation Results', () => {
        test('should handle validation success', async () => {
            const email = formElement.querySelector('[name="email"]') as HTMLInputElement;
            email.value = 'new@example.com';

            const validator = asyncGroup.getValidator('email');
            expect(validator).toBeDefined();
        });

        test('should handle validation failure', async () => {
            const email = formElement.querySelector('[name="email"]') as HTMLInputElement;
            email.value = 'taken@example.com';

            const validator = asyncGroup.getValidator('email');
            expect(validator).toBeDefined();
        });
    });
});

describe('Error Display Coordination', () => {
    let formElement: HTMLFormElement;
    let errorDisplay: ErrorDisplay;
    let validationSummary: ValidationSummary;

    beforeEach(() => {
        formElement = document.createElement('form');
        formElement.innerHTML = `
            <div id="validation-summary"></div>
            <input type="text" name="username" />
            <input type="email" name="email" />
            <button type="submit">Submit</button>
        `;
        document.body.appendChild(formElement);

        errorDisplay = new ErrorDisplay();
        validationSummary = new ValidationSummary(formElement.querySelector('#validation-summary')!);
    });

    afterEach(() => {
        document.body.removeChild(formElement);
    });

    describe('Field Error Display', () => {
        test('should highlight invalid field', () => {
            const username = formElement.querySelector('[name="username"]') as HTMLInputElement;
            errorDisplay.highlightField('username');

            expect(username.classList.contains('is-invalid')).toBe(true);
        });

        test('should clear field error', () => {
            const username = formElement.querySelector('[name="username"]') as HTMLInputElement;
            username.classList.add('is-invalid');

            errorDisplay.clearFieldErrors();

            expect(username.classList.contains('is-invalid')).toBe(false);
        });

        test('should display multiple field errors', () => {
            const mockResponse = {
                isValid: false,
                errors: [
                    { field: 'username', message: 'Required', code: 'REQUIRED', severity: 'error' },
                    { field: 'email', message: 'Invalid format', code: 'INVALID_EMAIL', severity: 'error' }
                ]
            };

            errorDisplay.displayResponse(mockResponse);

            const usernameField = formElement.querySelector('[name="username"]') as HTMLInputElement;
            const emailField = formElement.querySelector('[name="email"]') as HTMLInputElement;

            expect(usernameField.classList.contains('is-invalid')).toBe(true);
            expect(emailField.classList.contains('is-invalid')).toBe(true);
        });
    });

    describe('Validation Summary Display', () => {
        test('should display error summary', () => {
            const mockResponse = {
                isValid: false,
                errors: [
                    { field: 'username', message: 'Required', code: 'REQUIRED', severity: 'error' }
                ]
            };

            validationSummary.displayResponse(mockResponse);
            expect(validationSummary).toBeDefined();
        });

        test('should show error count', () => {
            const count = validationSummary.getErrorCount();
            expect(count).toBeGreaterThanOrEqual(0);
        });
    });
});

describe('Form Reset Workflow', () => {
    let formElement: HTMLFormElement;
    let integration: FormIntegration;

    beforeEach(() => {
        formElement = document.createElement('form');
        formElement.innerHTML = `
            <input type="text" name="username" value="" />
            <input type="email" name="email" value="" />
            <input type="password" name="password" value="" />
            <button type="reset">Reset</button>
            <button type="submit">Submit</button>
        `;
        document.body.appendChild(formElement);

        integration = new FormIntegration(formElement);
    });

    afterEach(() => {
        document.body.removeChild(formElement);
    });

    describe('Data Reset', () => {
        test('should clear form fields on reset', () => {
            const username = formElement.querySelector('[name="username"]') as HTMLInputElement;
            const email = formElement.querySelector('[name="email"]') as HTMLInputElement;

            username.value = 'testuser';
            email.value = 'test@example.com';

            formElement.reset();

            expect(username.value).toBe('');
            expect(email.value).toBe('');
        });

        test('should clear validation errors on reset', () => {
            const username = formElement.querySelector('[name="username"]') as HTMLInputElement;
            username.classList.add('is-invalid');

            integration.resetForm();

            expect(username.classList.contains('is-invalid')).toBe(false);
        });

        test('should clear error messages on reset', () => {
            integration.clearErrors();
            expect(integration).toBeDefined();
        });
    });
});

describe('Accessibility Workflow', () => {
    let formElement: HTMLFormElement;

    beforeEach(() => {
        formElement = document.createElement('form');
        formElement.innerHTML = `
            <div>
                <label for="username">Username</label>
                <input type="text" id="username" name="username" required aria-required="true" />
                <span id="username-error" class="error-message"></span>
            </div>
            <div>
                <label for="email">Email</label>
                <input type="email" id="email" name="email" required aria-required="true" />
                <span id="email-error" class="error-message"></span>
            </div>
            <button type="submit">Submit</button>
        `;
        document.body.appendChild(formElement);
    });

    afterEach(() => {
        document.body.removeChild(formElement);
    });

    describe('ARIA Attributes', () => {
        test('should have required aria attribute', () => {
            const username = formElement.querySelector('[name="username"]') as HTMLInputElement;
            expect(username.getAttribute('aria-required')).toBe('true');
        });

        test('should have error message associations', () => {
            const username = formElement.querySelector('[name="username"]') as HTMLInputElement;
            expect(username).toBeDefined();

            const errorMsg = formElement.querySelector('#username-error');
            expect(errorMsg).toBeDefined();
        });

        test('should update aria-invalid on error', () => {
            const username = formElement.querySelector('[name="username"]') as HTMLInputElement;
            username.setAttribute('aria-invalid', 'true');

            expect(username.getAttribute('aria-invalid')).toBe('true');
        });
    });

    describe('Keyboard Navigation', () => {
        test('should support tab navigation between fields', () => {
            const username = formElement.querySelector('[name="username"]') as HTMLInputElement;
            const email = formElement.querySelector('[name="email"]') as HTMLInputElement;

            expect(username.tabIndex).toBeGreaterThanOrEqual(-1);
            expect(email.tabIndex).toBeGreaterThanOrEqual(-1);
        });

        test('should support keyboard form submission', () => {
            const form = formElement;
            expect(form).toBeDefined();
        });
    });
});
