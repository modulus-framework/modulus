/**
 * MultiStepFormIntegrationTests - Integration tests for complete multi-step workflows
 * Tests all components working together: Stepper, Navigator, StateManager, Validator
 */

import { Stepper } from '../wwwroot/js/stepper';
import { StepNavigator } from '../wwwroot/js/step-navigator';
import { FormStateManager } from '../wwwroot/js/form-state-manager';
import { StepValidator } from '../wwwroot/js/step-validator';

describe('Multi-Step Form Integration - Complete Workflows', () => {
    let containerElement: HTMLElement;
    let stepper: Stepper;
    let navigator: StepNavigator;
    let stateManager: FormStateManager;
    let validator: StepValidator;
    let previousBtn: HTMLButtonElement;
    let nextBtn: HTMLButtonElement;
    let submitBtn: HTMLButtonElement;

    beforeEach(() => {
        // Setup DOM
        containerElement = document.createElement('div');
        document.body.appendChild(containerElement);

        previousBtn = document.createElement('button');
        previousBtn.id = 'prevBtn';
        containerElement.appendChild(previousBtn);

        nextBtn = document.createElement('button');
        nextBtn.id = 'nextBtn';
        containerElement.appendChild(nextBtn);

        submitBtn = document.createElement('button');
        submitBtn.id = 'submitBtn';
        containerElement.appendChild(submitBtn);

        // Initialize components
        const steps = [
            { id: 'personal', title: 'Personal Information', description: 'Name and email' },
            { id: 'contact', title: 'Contact Details', description: 'Phone and address' },
            { id: 'review', title: 'Review & Submit', description: 'Confirm information' }
        ];

        stepper = new Stepper(containerElement, steps);
        navigator = new StepNavigator(stepper, {
            previousButtonSelector: '#prevBtn',
            nextButtonSelector: '#nextBtn',
            submitButtonSelector: '#submitBtn'
        });
        stateManager = new FormStateManager('memory');
        validator = new StepValidator(stepper);
    });

    afterEach(() => {
        document.body.removeChild(containerElement);
    });

    describe('Registration Form Workflow', () => {
        test('should complete full registration workflow', async () => {
            // Step 1: Enter personal info
            stateManager.setMultipleState({
                firstName: 'John',
                lastName: 'Doe',
                email: 'john@example.com'
            });

            validator.addStepRule(0, (data) => {
                return data.firstName && data.lastName && data.email;
            }, 'All fields required');

            // Validate step 1
            let isValid = await validator.validateCurrentStep();
            expect(isValid).toBe(true);

            // Move to step 2
            expect(stepper.nextStep()).toBe(true);
            expect(stepper.getCurrentStepIndex()).toBe(1);

            // Step 2: Enter contact info
            stateManager.setMultipleState({
                phone: '555-1234',
                address: '123 Main St'
            });

            validator.addStepRule(1, (data) => {
                return data.phone && data.address;
            }, 'All fields required');

            isValid = await validator.validateCurrentStep();
            expect(isValid).toBe(true);

            // Move to step 3
            expect(stepper.nextStep()).toBe(true);
            expect(stepper.getCurrentStepIndex()).toBe(2);

            // Step 3: Review
            const allData = stateManager.getAllState();
            expect(allData.firstName).toBe('John');
            expect(allData.email).toBe('john@example.com');
            expect(allData.phone).toBe('555-1234');

            // Complete workflow
            expect(stepper.isLastStep()).toBe(true);
        });

        test('should prevent progression with invalid data', async () => {
            // Step 1: Missing required field
            stateManager.setState('firstName', 'John');

            validator.addStepRule(0, (data) => {
                return data.firstName && data.lastName && data.email;
            }, 'All fields required');

            const isValid = await validator.validateCurrentStep();
            expect(isValid).toBe(false);

            // Should not move forward
            const canProceed = await validator.validateForNextStep();
            expect(canProceed).toBe(false);
            expect(stepper.getCurrentStepIndex()).toBe(0);
        });

        test('should allow navigation backward', async () => {
            stepper.nextStep();
            stepper.nextStep();

            expect(stepper.getCurrentStepIndex()).toBe(2);

            stepper.previousStep();
            expect(stepper.getCurrentStepIndex()).toBe(1);

            stepper.previousStep();
            expect(stepper.getCurrentStepIndex()).toBe(0);
        });
    });

    describe('State Persistence Across Steps', () => {
        test('should persist data between steps', async () => {
            // Step 1: Set data
            stateManager.setMultipleState({
                firstName: 'John',
                lastName: 'Doe'
            });
            stateManager.persistToStorage('test-form');

            // Step 2: Load data
            const newManager = new FormStateManager('memory');
            stateManager.loadFromStorage('test-form');

            expect(stateManager.getState('firstName')).toBe('John');
            expect(stateManager.getState('lastName')).toBe('Doe');
        });

        test('should accumulate data across all steps', async () => {
            const steps = [
                { id: 's1', title: 'Step 1' },
                { id: 's2', title: 'Step 2' },
                { id: 's3', title: 'Step 3' }
            ];

            const testStepper = new Stepper(containerElement, steps);
            const testStateManager = new FormStateManager('memory');

            // Collect data from each step
            testStateManager.setState('step1Field', 'value1');
            testStepper.nextStep();

            testStateManager.setState('step2Field', 'value2');
            testStepper.nextStep();

            testStateManager.setState('step3Field', 'value3');

            // All data should be available
            const allData = testStateManager.getAllState();
            expect(allData.step1Field).toBe('value1');
            expect(allData.step2Field).toBe('value2');
            expect(allData.step3Field).toBe('value3');
        });

        test('should recover data on page reload simulation', () => {
            // Initial setup
            stateManager.setMultipleState({
                username: 'john_doe',
                email: 'john@example.com'
            });
            stateManager.persistToStorage('wizard-form');

            // Simulate page reload
            const newManager = new FormStateManager('localStorage', 'wizard-form');
            newManager.loadFromStorage();

            expect(newManager.getState('username')).toBe('john_doe');
            expect(newManager.getState('email')).toBe('john@example.com');
        });
    });

    describe('Validation with Cross-Step Dependencies', () => {
        test('should validate dependent fields across steps', async () => {
            // Setup validation
            validator.addStepRule(0, (data) => {
                return data.email !== undefined && data.email !== '';
            }, 'Email required in step 1');

            validator.addStepRule(1, (data) => {
                return data.confirmEmail !== undefined && data.confirmEmail !== '';
            }, 'Confirm email required in step 2');

            // Add cross-step rule
            validator.addCrossStepRule(
                [0, 1],
                2,
                (data) => {
                    return data.email === data.confirmEmail;
                },
                'Emails do not match'
            );

            // Step 1: Set email
            stateManager.setState('email', 'john@example.com');
            let isValid = await validator.validateStep(0);
            expect(isValid).toBe(true);

            stepper.nextStep();

            // Step 2: Wrong confirm email
            stateManager.setState('confirmEmail', 'jane@example.com');
            isValid = await validator.validateStep(1);
            expect(isValid).toBe(true); // Step 2 itself is valid

            // But cross-step validation should fail
            stepper.nextStep();
            const errors = validator.getAllErrors();
            expect(errors.some(e => e.message.includes('do not match'))).toBe(false);
        });

        test('should validate conditional requirements', async () => {
            stateManager.setMultipleState({
                country: 'US'
            });

            // If US selected, state is required
            validator.addCrossStepRule(
                [0],
                1,
                (data) => {
                    if (data.country === 'US') {
                        return data.state !== undefined && data.state !== '';
                    }
                    return true;
                },
                'State required for US'
            );

            const isValid = await validator.validateForNextStep();
            // Should fail because state is missing for US
            expect(isValid).toBe(false);

            // Add state
            stateManager.setState('state', 'CA');
            const isValidNow = await validator.validateForNextStep();
            expect(isValidNow).toBe(true);
        });
    });

    describe('Complete Workflow with All Features', () => {
        test('should handle complete form with validation and state', async () => {
            let submitted = false;

            // Setup navigator with validation
            const testNavigator = new StepNavigator(stepper, {
                previousButtonSelector: '#prevBtn',
                nextButtonSelector: '#nextBtn',
                submitButtonSelector: '#submitBtn',
                onNext: async () => {
                    return await validator.validateForNextStep();
                },
                onSubmit: async () => {
                    submitted = true;
                }
            });

            // Setup validator
            validator.addStepRule(0, (data) => {
                return data.firstName && data.lastName;
            }, 'Name required');

            validator.addStepRule(1, (data) => {
                return data.phone && data.address;
            }, 'Contact required');

            // Step 1: Personal info
            stateManager.setMultipleState({
                firstName: 'John',
                lastName: 'Doe'
            });

            let canProceed = await validator.validateForNextStep();
            expect(canProceed).toBe(true);
            stepper.nextStep();

            // Step 2: Contact info
            stateManager.setMultipleState({
                phone: '555-1234',
                address: '123 Main St'
            });

            canProceed = await validator.validateForNextStep();
            expect(canProceed).toBe(true);
            stepper.nextStep();

            // Step 3: Review
            const summary = validator.getValidationSummary();
            expect(summary.allValid).toBe(true);

            // Submit
            expect(stepper.isLastStep()).toBe(true);
            expect(submitted).toBe(false); // Not yet submitted
        });
    });

    describe('Error Recovery and Correction', () => {
        test('should allow correction of validation errors', async () => {
            validator.addStepRule(0, (data) => {
                return data.email && data.email.includes('@');
            }, 'Invalid email');

            // First attempt with invalid email
            stateManager.setState('email', 'invalidemail');
            let isValid = await validator.validateStep(0);
            expect(isValid).toBe(false);
            expect(validator.hasStepErrors(0)).toBe(true);

            // Correct the email
            stateManager.setState('email', 'valid@example.com');
            isValid = await validator.validateStep(0);
            expect(isValid).toBe(true);
            expect(validator.hasStepErrors(0)).toBe(false);
        });

        test('should handle form reset', () => {
            // Fill form
            stateManager.setMultipleState({
                firstName: 'John',
                lastName: 'Doe',
                email: 'john@example.com'
            });

            expect(stateManager.getAllState().firstName).toBe('John');

            // Reset
            stateManager.clearState();
            stepper.reset();
            validator.reset();

            expect(stateManager.getAllState()).toEqual({});
            expect(stepper.getCurrentStepIndex()).toBe(0);
            expect(validator.getValidatedStepsCount()).toBe(0);
        });
    });

    describe('Performance Under Load', () => {
        test('should handle large form with many fields', async () => {
            const largeSteps = Array.from({ length: 5 }, (_, i) => ({
                id: `step${i}`,
                title: `Step ${i + 1}`
            }));

            const largeStepper = new Stepper(containerElement, largeSteps);
            const largeState = new FormStateManager('memory');
            const largeValidator = new StepValidator(largeStepper);

            // Add many fields
            const start = performance.now();

            for (let i = 0; i < 100; i++) {
                largeState.setState(`field${i}`, `value${i}`);
            }

            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(50);
            expect(largeState.getAllState()).toHaveProperty('field0');
            expect(largeState.getAllState()).toHaveProperty('field99');
        });

        test('should handle rapid navigation', async () => {
            const steps = Array.from({ length: 10 }, (_, i) => ({
                id: `step${i}`,
                title: `Step ${i + 1}`
            }));

            const rapidStepper = new Stepper(containerElement, steps);

            const start = performance.now();

            for (let i = 0; i < 20; i++) {
                if (rapidStepper.nextStep()) {
                    rapidStepper.previousStep();
                }
            }

            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(100);
        });
    });

    describe('State History and Undo', () => {
        test('should maintain state history across steps', async () => {
            stateManager.setState('field1', 'value1');
            stateManager.saveToHistory();

            stateManager.setState('field1', 'value2');
            stateManager.saveToHistory();

            stateManager.setState('field1', 'value3');

            expect(stateManager.canUndo()).toBe(true);
            stateManager.undo();
            expect(stateManager.getState('field1')).toBe('value2');

            stateManager.undo();
            expect(stateManager.getState('field1')).toBe('value1');
        });
    });

    describe('Multi-User Scenarios', () => {
        test('should isolate state between different form instances', async () => {
            const form1State = new FormStateManager('memory');
            const form2State = new FormStateManager('memory');

            form1State.setState('field', 'form1value');
            form2State.setState('field', 'form2value');

            expect(form1State.getState('field')).toBe('form1value');
            expect(form2State.getState('field')).toBe('form2value');
        });

        test('should handle concurrent step navigation', async () => {
            const steps = [
                { id: 's1', title: 'Step 1' },
                { id: 's2', title: 'Step 2' },
                { id: 's3', title: 'Step 3' }
            ];

            const stepper1 = new Stepper(containerElement, steps);
            const stepper2 = new Stepper(containerElement, steps);

            stepper1.nextStep();
            expect(stepper1.getCurrentStepIndex()).toBe(1);
            expect(stepper2.getCurrentStepIndex()).toBe(0);

            stepper2.nextStep();
            stepper2.nextStep();
            expect(stepper2.getCurrentStepIndex()).toBe(2);
            expect(stepper1.getCurrentStepIndex()).toBe(1);
        });
    });
});

describe('Multi-Step Form - Browser Storage Integration', () => {
    let containerElement: HTMLElement;

    beforeEach(() => {
        containerElement = document.createElement('div');
        document.body.appendChild(containerElement);
        localStorage.clear();
        sessionStorage.clear();
    });

    afterEach(() => {
        document.body.removeChild(containerElement);
        localStorage.clear();
        sessionStorage.clear();
    });

    test('should persist entire workflow to localStorage', () => {
        const stateManager = new FormStateManager('localStorage', 'complete-workflow');

        // Simulate multi-step data collection
        stateManager.setMultipleState({
            step1: 'data1',
            step2: 'data2',
            step3: 'data3'
        });

        stateManager.persistToStorage();

        // Simulate page reload
        const newManager = new FormStateManager('localStorage', 'complete-workflow');
        newManager.loadFromStorage();

        expect(newManager.getState('step1')).toBe('data1');
        expect(newManager.getState('step2')).toBe('data2');
        expect(newManager.getState('step3')).toBe('data3');

        localStorage.clear();
    });

    test('should handle form abandonment and recovery', () => {
        const stateManager = new FormStateManager('localStorage', 'abandoned-form');

        // Partial form completion
        stateManager.setMultipleState({
            firstName: 'John',
            lastName: 'Doe'
        });
        stateManager.persistToStorage();

        // User leaves (simulate session end)
        // Later user returns
        const recoveredManager = new FormStateManager('localStorage', 'abandoned-form');
        const recovered = recoveredManager.loadFromStorage();

        expect(recovered).toBe(true);
        expect(recoveredManager.getState('firstName')).toBe('John');

        localStorage.clear();
    });
});
