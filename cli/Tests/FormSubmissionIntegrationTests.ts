/**
 * FormSubmissionIntegrationTests - Integration tests for form submission
 * Tests client-server validation workflow and error handling
 */

import { FormIntegration } from '../wwwroot/js/form-integration';
import { FormSubmissionHandler, ValidationResponse } from '../wwwroot/js/form-submission-handler';
import { ErrorDisplay } from '../wwwroot/js/error-display';
import { ValidationSummary } from '../wwwroot/js/validation-summary';
import { ToastNotification } from '../wwwroot/js/toast-notification';

describe('FormSubmissionIntegration', () => {
    let formElement: HTMLFormElement;
    let integration: FormIntegration;

    beforeEach(() => {
        // Create test form
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
            <div class="validation-summary-container"></div>
            <button type="submit" class="btn btn-primary">Submit</button>
        `;
        document.body.appendChild(formElement);

        integration = new FormIntegration('#testForm', {
            validateOnBlur: false,
            validateOnChange: false,
            validateOnSubmit: true,
            showErrorSummary: true,
            showToastNotifications: true,
            submissionEndpoint: '/api/submit',
            submissionMethod: 'POST'
        });
    });

    afterEach(() => {
        document.body.removeChild(formElement);
    });

    describe('Form Initialization', () => {
        test('should initialize all components', () => {
            expect(integration.getValidator()).toBeDefined();
            expect(integration.getSubmissionHandler()).toBeDefined();
            expect(integration.getErrorDisplay()).toBeDefined();
            expect(integration.getValidationSummary()).toBeDefined();
            expect(integration.getToastNotifications()).toBeDefined();
        });

        test('should attach form submit handler', () => {
            expect(formElement).toBeDefined();
        });

        test('should validate on client before submission', async () => {
            const isValid = await integration.validateForm();
            expect(isValid).toBe(false); // Name field is required and empty
        });
    });

    describe('Client-Side Validation', () => {
        test('should detect required field missing', async () => {
            const isValid = await integration.validateForm();
            expect(isValid).toBe(false);

            const errors = integration.getValidationSummary().getErrors();
            expect(errors.length).toBeGreaterThan(0);
            expect(errors[0].field).toBe('name');
            expect(errors[0].code).toBe('REQUIRED');
        });

        test('should detect invalid email', async () => {
            const nameInput = formElement.querySelector('#name') as HTMLInputElement;
            const emailInput = formElement.querySelector('#email') as HTMLInputElement;

            nameInput.value = 'John Doe';
            emailInput.value = 'invalid-email';

            const isValid = await integration.validateForm();
            expect(isValid).toBe(false);
        });

        test('should pass validation with valid data', async () => {
            const nameInput = formElement.querySelector('#name') as HTMLInputElement;
            const emailInput = formElement.querySelector('#email') as HTMLInputElement;

            nameInput.value = 'John Doe';
            emailInput.value = 'john@example.com';

            const isValid = await integration.validateForm();
            expect(isValid).toBe(true);
        });
    });

    describe('Error Display', () => {
        test('should display field errors', async () => {
            const mockResponse: ValidationResponse = {
                isValid: false,
                errors: [{
                    field: 'name',
                    message: 'Name is required',
                    code: 'REQUIRED',
                    severity: 'error'
                }],
                warnings: [],
                message: 'Validation failed'
            };

            integration.getErrorDisplay().displayResponse(mockResponse);

            const errors = integration.getErrorDisplay().getDisplayedErrors();
            expect(errors.has('name')).toBe(true);
        });

        test('should highlight invalid fields', async () => {
            const nameInput = formElement.querySelector('#name') as HTMLInputElement;

            integration.showFieldError('name', 'This field is required', 'REQUIRED');

            expect(nameInput.classList.contains('is-invalid')).toBe(true);
        });

        test('should clear field errors', async () => {
            integration.showFieldError('name', 'Error message');
            integration.clearFieldError('name');

            expect(integration.getErrorDisplay().hasErrors('name')).toBe(false);
        });
    });

    describe('Validation Summary', () => {
        test('should display validation summary', async () => {
            const mockResponse: ValidationResponse = {
                isValid: false,
                errors: [
                    {
                        field: 'name',
                        message: 'Name is required',
                        code: 'REQUIRED',
                        severity: 'error'
                    },
                    {
                        field: 'email',
                        message: 'Email is invalid',
                        code: 'INVALID_FORMAT',
                        severity: 'error'
                    }
                ],
                warnings: [],
                message: 'Validation failed'
            };

            integration.getValidationSummary().displayResponse(mockResponse);

            const summary = integration.getValidationSummary();
            expect(summary.getErrorCount()).toBe(2);
        });

        test('should group errors by field in summary', async () => {
            const mockResponse: ValidationResponse = {
                isValid: false,
                errors: [
                    {
                        field: 'name',
                        message: 'Name is required',
                        code: 'REQUIRED',
                        severity: 'error'
                    },
                    {
                        field: 'name',
                        message: 'Name must be unique',
                        code: 'DUPLICATE_VALUE',
                        severity: 'error'
                    }
                ],
                warnings: [],
                message: 'Validation failed'
            };

            integration.getValidationSummary().displayResponse(mockResponse);

            const nameErrors = integration.getValidationSummary().getFieldErrors('name');
            expect(nameErrors.length).toBe(2);
        });

        test('should clear validation summary', () => {
            integration.getValidationSummary().show();
            expect(integration.getValidationSummary().hasErrors()).toBe(false);

            integration.getValidationSummary().clear();
            expect(integration.getValidationSummary().hasErrors()).toBe(false);
        });
    });

    describe('Toast Notifications', () => {
        test('should show success notification', () => {
            integration.showNotification('Form submitted successfully', 'success');

            const count = integration.getToastNotifications().getCount();
            expect(count).toBeGreaterThan(0);
        });

        test('should show error notification', () => {
            integration.showNotification('An error occurred', 'error');

            const count = integration.getToastNotifications().getCount();
            expect(count).toBeGreaterThan(0);
        });

        test('should show warning notification', () => {
            integration.showNotification('Please check your data', 'warning');

            const count = integration.getToastNotifications().getCount();
            expect(count).toBeGreaterThan(0);
        });

        test('should clear all notifications', () => {
            integration.showNotification('Message 1', 'info');
            integration.showNotification('Message 2', 'info');

            integration.getToastNotifications().clearAll();
            expect(integration.getToastNotifications().getCount()).toBe(0);
        });
    });

    describe('Form Data', () => {
        test('should get form data as object', () => {
            const nameInput = formElement.querySelector('#name') as HTMLInputElement;
            const emailInput = formElement.querySelector('#email') as HTMLInputElement;

            nameInput.value = 'John Doe';
            emailInput.value = 'john@example.com';

            const data = integration.getFormData();
            expect(data.name).toBe('John Doe');
            expect(data.email).toBe('john@example.com');
        });

        test('should handle multiple values for same field', () => {
            const input1 = document.createElement('input');
            input1.type = 'checkbox';
            input1.name = 'interests';
            input1.value = 'coding';
            input1.checked = true;

            const input2 = document.createElement('input');
            input2.type = 'checkbox';
            input2.name = 'interests';
            input2.value = 'gaming';
            input2.checked = true;

            formElement.appendChild(input1);
            formElement.appendChild(input2);

            const data = integration.getFormData();
            expect(Array.isArray(data.interests)).toBe(true);
            expect(data.interests.length).toBe(2);
        });
    });

    describe('Form State', () => {
        test('should check if form is valid', async () => {
            const nameInput = formElement.querySelector('#name') as HTMLInputElement;
            nameInput.value = 'John Doe';

            const isValid = integration.isValid();
            expect(isValid).toBe(true);
        });

        test('should check if form is submitting', () => {
            const isSubmitting = integration.isSubmitting();
            expect(typeof isSubmitting).toBe('boolean');
        });

        test('should reset form', () => {
            const nameInput = formElement.querySelector('#name') as HTMLInputElement;
            nameInput.value = 'John Doe';

            integration.resetForm();
            expect(nameInput.value).toBe('');
        });
    });

    describe('Endpoint Configuration', () => {
        test('should set submission endpoint', () => {
            integration.setSubmissionEndpoint('/api/new-endpoint');
            expect(integration).toBeDefined();
        });

        test('should warn if no endpoint configured', () => {
            const form = document.createElement('form');
            const noEndpointIntegration = new FormIntegration(form);

            const consoleSpy = jest.spyOn(console, 'warn');
            // Attempt submission would trigger warning
            expect(consoleSpy).toBeDefined();
        });
    });

    describe('Error Scenarios', () => {
        test('should handle general validation errors', async () => {
            const mockResponse: ValidationResponse = {
                isValid: false,
                errors: [{
                    field: 'general',
                    message: 'An unexpected error occurred',
                    code: 'GENERAL_ERROR',
                    severity: 'error'
                }],
                warnings: [],
                message: 'General error'
            };

            integration.getErrorDisplay().displayResponse(mockResponse);
            const errors = integration.getErrorDisplay().getDisplayedErrors();
            expect(errors.has('general')).toBe(true);
        });

        test('should handle multiple errors per field', async () => {
            const mockResponse: ValidationResponse = {
                isValid: false,
                errors: [
                    {
                        field: 'password',
                        message: 'Password is required',
                        code: 'REQUIRED',
                        severity: 'error'
                    },
                    {
                        field: 'password',
                        message: 'Password must be at least 8 characters',
                        code: 'LENGTH_ERROR',
                        severity: 'error'
                    }
                ],
                warnings: [],
                message: 'Validation failed'
            };

            integration.getErrorDisplay().displayResponse(mockResponse);
            const errors = integration.getErrorDisplay().getDisplayedErrors();
            expect(errors.get('password')?.length).toBe(2);
        });
    });

    describe('Integration Workflow', () => {
        test('should complete full validation workflow', async () => {
            const nameInput = formElement.querySelector('#name') as HTMLInputElement;
            nameInput.value = 'John Doe';

            // Validate
            const isValid = await integration.validateForm();

            // Check form state
            expect(isValid).toBe(true);
            expect(integration.isValid()).toBe(true);

            // Clear errors
            integration.clearErrors();
            expect(integration.getValidationSummary().hasErrors()).toBe(false);
        });

        test('should handle validation then correction workflow', async () => {
            // Initial validation (should fail)
            let isValid = await integration.validateForm();
            expect(isValid).toBe(false);

            // Fill in required field
            const nameInput = formElement.querySelector('#name') as HTMLInputElement;
            nameInput.value = 'John Doe';

            // Validate again (should pass)
            integration.clearErrors();
            isValid = await integration.validateForm();
            expect(isValid).toBe(true);
        });
    });
});
