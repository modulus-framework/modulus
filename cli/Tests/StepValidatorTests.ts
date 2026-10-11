/**
 * StepValidatorTests - Tests for step validation and progression
 * Covers step-level, cross-step, and conditional validation
 */

import { Stepper } from '../wwwroot/js/stepper';
import { StepValidator } from '../wwwroot/js/step-validator';

describe('StepValidator - Basic Validation', () => {
    let containerElement: HTMLElement;
    let stepper: Stepper;
    let validator: StepValidator;

    beforeEach(() => {
        containerElement = document.createElement('div');
        document.body.appendChild(containerElement);

        const steps = [
            { id: 'step1', title: 'Step 1' },
            { id: 'step2', title: 'Step 2' },
            { id: 'step3', title: 'Step 3' }
        ];

        stepper = new Stepper(containerElement, steps);
        validator = new StepValidator(stepper);
    });

    afterEach(() => {
        document.body.removeChild(containerElement);
    });

    test('should initialize step validator', () => {
        expect(validator).toBeDefined();
    });

    test('should add step validation rule', () => {
        validator.addStepRule(0, (data) => true, 'Validation failed');
        expect(validator).toBeDefined();
    });

    test('should validate step with passing rule', async () => {
        validator.addStepRule(0, (data) => true, 'Should pass');
        const result = await validator.validateStep(0);
        expect(result).toBe(true);
    });

    test('should validate step with failing rule', async () => {
        validator.addStepRule(0, (data) => false, 'This validation fails');
        const result = await validator.validateStep(0);
        expect(result).toBe(false);
    });

    test('should collect validation errors', async () => {
        validator.addStepRule(0, (data) => false, 'Error 1');
        validator.addStepRule(0, (data) => false, 'Error 2');

        await validator.validateStep(0);
        const errors = validator.getStepErrors(0);

        expect(errors.length).toBeGreaterThan(0);
    });

    test('should clear step errors', async () => {
        validator.addStepRule(0, (data) => false, 'Validation error');

        await validator.validateStep(0);
        expect(validator.getStepErrors(0).length).toBeGreaterThan(0);

        validator.clearStepErrors(0);
        expect(validator.getStepErrors(0).length).toBe(0);
    });

    test('should check if step has errors', async () => {
        validator.addStepRule(0, (data) => false, 'Error');

        await validator.validateStep(0);
        expect(validator.hasStepErrors(0)).toBe(true);
    });

    test('should track validated steps', async () => {
        validator.addStepRule(0, (data) => true, 'Pass');

        expect(validator.isStepValidated(0)).toBe(false);

        await validator.validateStep(0);
        expect(validator.isStepValidated(0)).toBe(true);
    });

    test('should get validated steps count', async () => {
        validator.addStepRule(0, (data) => true, 'Pass');
        validator.addStepRule(1, (data) => true, 'Pass');

        await validator.validateStep(0);
        await validator.validateStep(1);

        expect(validator.getValidatedStepsCount()).toBe(2);
    });
});

describe('StepValidator - Current Step Validation', () => {
    let containerElement: HTMLElement;
    let stepper: Stepper;
    let validator: StepValidator;

    beforeEach(() => {
        containerElement = document.createElement('div');
        document.body.appendChild(containerElement);

        const steps = [
            { id: 'step1', title: 'Step 1' },
            { id: 'step2', title: 'Step 2' }
        ];

        stepper = new Stepper(containerElement, steps);
        validator = new StepValidator(stepper);
    });

    afterEach(() => {
        document.body.removeChild(containerElement);
    });

    test('should validate current step', async () => {
        validator.addStepRule(0, (data) => true, 'Pass');

        const result = await validator.validateCurrentStep();
        expect(result).toBe(true);
    });

    test('should prevent navigation with invalid step', async () => {
        validator.addStepRule(0, (data) => false, 'Invalid');

        const canProceed = await validator.validateForNextStep();
        expect(canProceed).toBe(false);
        expect(stepper.getCurrentStepIndex()).toBe(0);
    });

    test('should allow navigation with valid step', async () => {
        validator.addStepRule(0, (data) => true, 'Valid');

        const canProceed = await validator.validateForNextStep();
        expect(canProceed).toBe(true);
    });
});

describe('StepValidator - Cross-Step Validation', () => {
    let containerElement: HTMLElement;
    let stepper: Stepper;
    let validator: StepValidator;

    beforeEach(() => {
        containerElement = document.createElement('div');
        document.body.appendChild(containerElement);

        const steps = [
            { id: 'step1', title: 'Personal' },
            { id: 'step2', title: 'Contact' },
            { id: 'step3', title: 'Review' }
        ];

        stepper = new Stepper(containerElement, steps);
        validator = new StepValidator(stepper);
    });

    afterEach(() => {
        document.body.removeChild(containerElement);
    });

    test('should add cross-step validation rule', () => {
        validator.addCrossStepRule(
            [0],
            1,
            (data) => true,
            'Cross-step validation'
        );
        expect(validator).toBeDefined();
    });

    test('should validate cross-step rule on progression', async () => {
        validator.addStepRule(0, (data) => true, 'Pass');
        validator.addCrossStepRule(
            [0],
            1,
            (data) => true,
            'Cross-step passed'
        );

        const result = await validator.validateForNextStep();
        expect(result).toBe(true);
    });

    test('should block progression with failing cross-step rule', async () => {
        validator.addStepRule(0, (data) => true, 'Pass');
        validator.addCrossStepRule(
            [0],
            1,
            (data) => false,
            'Cross-step validation failed'
        );

        const result = await validator.validateForNextStep();
        expect(result).toBe(false);
    });

    test('should validate multiple source steps', async () => {
        validator.addStepRule(0, (data) => true, 'Pass');
        validator.addStepRule(1, (data) => true, 'Pass');
        validator.addCrossStepRule(
            [0, 1],
            2,
            (data) => true,
            'Multi-source validation'
        );

        stepper.nextStep(); // Move to step 1
        stepper.nextStep(); // Move to step 2

        expect(stepper.getCurrentStepIndex()).toBe(2);
    });
});

describe('StepValidator - Full Form Validation', () => {
    let containerElement: HTMLElement;
    let stepper: Stepper;
    let validator: StepValidator;

    beforeEach(() => {
        containerElement = document.createElement('div');
        document.body.appendChild(containerElement);

        const steps = [
            { id: 'step1', title: 'Step 1' },
            { id: 'step2', title: 'Step 2' },
            { id: 'step3', title: 'Step 3' }
        ];

        stepper = new Stepper(containerElement, steps);
        validator = new StepValidator(stepper);
    });

    afterEach(() => {
        document.body.removeChild(containerElement);
    });

    test('should validate all steps before submission', async () => {
        validator.addStepRule(0, (data) => true, 'Pass');
        validator.addStepRule(1, (data) => true, 'Pass');
        validator.addStepRule(2, (data) => true, 'Pass');

        const result = await validator.validateAllSteps();
        expect(result).toBe(true);
    });

    test('should fail validation if any step invalid', async () => {
        validator.addStepRule(0, (data) => true, 'Pass');
        validator.addStepRule(1, (data) => false, 'Fail');
        validator.addStepRule(2, (data) => true, 'Pass');

        const result = await validator.validateAllSteps();
        expect(result).toBe(false);
    });

    test('should collect all errors on validation', async () => {
        validator.addStepRule(0, (data) => false, 'Error 1');
        validator.addStepRule(1, (data) => false, 'Error 2');
        validator.addStepRule(2, (data) => false, 'Error 3');

        await validator.validateAllSteps();
        const allErrors = validator.getAllErrors();

        expect(allErrors.length).toBeGreaterThanOrEqual(3);
    });
});

describe('StepValidator - Error Management', () => {
    let containerElement: HTMLElement;
    let stepper: Stepper;
    let validator: StepValidator;

    beforeEach(() => {
        containerElement = document.createElement('div');
        document.body.appendChild(containerElement);

        const steps = [
            { id: 'step1', title: 'Step 1' },
            { id: 'step2', title: 'Step 2' }
        ];

        stepper = new Stepper(containerElement, steps);
        validator = new StepValidator(stepper);
    });

    afterEach(() => {
        document.body.removeChild(containerElement);
    });

    test('should clear all errors', async () => {
        validator.addStepRule(0, (data) => false, 'Error');
        validator.addStepRule(1, (data) => false, 'Error');

        await validator.validateAllSteps();
        expect(validator.getAllErrors().length).toBeGreaterThan(0);

        validator.clearAllErrors();
        expect(validator.getAllErrors().length).toBe(0);
    });

    test('should reset validation state', async () => {
        validator.addStepRule(0, (data) => true, 'Pass');

        await validator.validateStep(0);
        expect(validator.getValidatedStepsCount()).toBe(1);

        validator.reset();
        expect(validator.getValidatedStepsCount()).toBe(0);
    });

    test('should trigger error callback on validation failure', (done) => {
        let errorFired = false;

        const validatorWithCallback = new StepValidator(stepper, undefined, {
            onValidationError: (errors) => {
                errorFired = true;
            }
        });

        validatorWithCallback.addStepRule(0, (data) => false, 'Error');

        validatorWithCallback.validateStep(0).then(() => {
            setTimeout(() => {
                expect(errorFired).toBe(true);
                done();
            }, 10);
        });
    });

    test('should trigger success callback on validation pass', (done) => {
        let successFired = false;

        const validatorWithCallback = new StepValidator(stepper, undefined, {
            onValidationSuccess: (stepIndex) => {
                successFired = true;
            }
        });

        validatorWithCallback.addStepRule(0, (data) => true, 'Pass');

        validatorWithCallback.validateStep(0).then(() => {
            setTimeout(() => {
                expect(successFired).toBe(true);
                done();
            }, 10);
        });
    });
});

describe('StepValidator - Async Validation', () => {
    let containerElement: HTMLElement;
    let stepper: Stepper;
    let validator: StepValidator;

    beforeEach(() => {
        containerElement = document.createElement('div');
        document.body.appendChild(containerElement);

        const steps = [
            { id: 'step1', title: 'Step 1' },
            { id: 'step2', title: 'Step 2' }
        ];

        stepper = new Stepper(containerElement, steps);
        validator = new StepValidator(stepper);
    });

    afterEach(() => {
        document.body.removeChild(containerElement);
    });

    test('should support async validation rules', async () => {
        validator.addStepRule(0, async (data) => {
            return new Promise(resolve => {
                setTimeout(() => resolve(true), 50);
            });
        }, 'Async validation');

        const result = await validator.validateStep(0);
        expect(result).toBe(true);
    });

    test('should handle async validation errors', async () => {
        validator.addStepRule(0, async (data) => {
            return new Promise(resolve => {
                setTimeout(() => resolve(false), 50);
            });
        }, 'Async validation failed');

        const result = await validator.validateStep(0);
        expect(result).toBe(false);
    });
});

describe('StepValidator - Validation Summary', () => {
    let containerElement: HTMLElement;
    let stepper: Stepper;
    let validator: StepValidator;

    beforeEach(() => {
        containerElement = document.createElement('div');
        document.body.appendChild(containerElement);

        const steps = [
            { id: 'step1', title: 'Step 1' },
            { id: 'step2', title: 'Step 2' },
            { id: 'step3', title: 'Step 3' }
        ];

        stepper = new Stepper(containerElement, steps);
        validator = new StepValidator(stepper);
    });

    afterEach(() => {
        document.body.removeChild(containerElement);
    });

    test('should generate validation summary', async () => {
        validator.addStepRule(0, (data) => true, 'Pass');
        validator.addStepRule(1, (data) => false, 'Fail');

        await validator.validateStep(0);
        await validator.validateStep(1);

        const summary = validator.getValidationSummary();

        expect(summary.totalErrors).toBeGreaterThan(0);
        expect(summary.allValid).toBe(false);
    });

    test('should calculate validation progress', async () => {
        validator.addStepRule(0, (data) => true, 'Pass');

        await validator.validateStep(0);

        const summary = validator.getValidationSummary();
        expect(summary.validationProgress).toBeGreaterThan(0);
    });

    test('should export validation state', async () => {
        validator.addStepRule(0, (data) => true, 'Pass');

        await validator.validateStep(0);

        const json = validator.exportValidationState();
        const parsed = JSON.parse(json);

        expect(parsed.validatedSteps).toBeDefined();
        expect(parsed.totalSteps).toBe(3);
    });

    test('should import validation state', () => {
        const json = JSON.stringify({
            stepErrors: [],
            validatedSteps: [0, 1],
            totalSteps: 3
        });

        const result = validator.importValidationState(json);
        expect(result).toBe(true);
        expect(validator.getValidatedStepsCount()).toBe(2);
    });
});

describe('StepValidator - Integration with Stepper', () => {
    let containerElement: HTMLElement;
    let stepper: Stepper;
    let validator: StepValidator;

    beforeEach(() => {
        containerElement = document.createElement('div');
        document.body.appendChild(containerElement);

        const steps = [
            { id: 'step1', title: 'Step 1' },
            { id: 'step2', title: 'Step 2' }
        ];

        stepper = new Stepper(containerElement, steps);
        validator = new StepValidator(stepper);
    });

    afterEach(() => {
        document.body.removeChild(containerElement);
    });

    test('should mark step invalid in stepper', async () => {
        validator.addStepRule(0, (data) => false, 'Invalid');

        await validator.validateStep(0);

        expect(stepper.getStepStatus(0)).toBe('error');
    });

    test('should prevent invalid step progression', async () => {
        validator.addStepRule(0, (data) => false, 'Invalid');

        const result = await validator.validateForNextStep();
        expect(result).toBe(false);
        expect(stepper.getCurrentStepIndex()).toBe(0);
    });
});
