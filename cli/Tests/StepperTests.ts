/**
 * StepperTests - Tests for stepper and navigator components
 * Covers step navigation, state management, and UI updates
 */

import { Stepper, Step } from '../wwwroot/js/stepper';
import { StepNavigator } from '../wwwroot/js/step-navigator';

describe('Stepper Component', () => {
    let containerElement: HTMLElement;

    beforeEach(() => {
        containerElement = document.createElement('div');
        document.body.appendChild(containerElement);
    });

    afterEach(() => {
        document.body.removeChild(containerElement);
    });

    describe('Initialization', () => {
        test('should create stepper with steps', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' },
                { id: 'step3', title: 'Step 3' }
            ];

            const stepper = new Stepper(containerElement, steps);

            expect(stepper.getStepCount()).toBe(3);
            expect(stepper.getCurrentStepIndex()).toBe(0);
        });

        test('should initialize with empty steps', () => {
            const stepper = new Stepper(containerElement);

            expect(stepper.getStepCount()).toBe(0);
        });

        test('should add steps individually', () => {
            const stepper = new Stepper(containerElement);

            stepper.addStep({ id: 'step1', title: 'Step 1' });
            stepper.addStep({ id: 'step2', title: 'Step 2' });

            expect(stepper.getStepCount()).toBe(2);
        });
    });

    describe('Step Navigation', () => {
        test('should move to next step when valid', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' },
                { id: 'step3', title: 'Step 3' }
            ];

            const stepper = new Stepper(containerElement, steps);

            expect(stepper.getCurrentStepIndex()).toBe(0);
            expect(stepper.nextStep()).toBe(true);
            expect(stepper.getCurrentStepIndex()).toBe(1);
        });

        test('should not move to next step when already at last step', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' }
            ];

            const stepper = new Stepper(containerElement, steps);

            stepper.nextStep();
            expect(stepper.nextStep()).toBe(false);
            expect(stepper.getCurrentStepIndex()).toBe(1);
        });

        test('should move to previous step', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' },
                { id: 'step3', title: 'Step 3' }
            ];

            const stepper = new Stepper(containerElement, steps);

            stepper.nextStep();
            stepper.nextStep();
            expect(stepper.getCurrentStepIndex()).toBe(2);

            stepper.previousStep();
            expect(stepper.getCurrentStepIndex()).toBe(1);
        });

        test('should not move to previous step when at first step', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' }
            ];

            const stepper = new Stepper(containerElement, steps);

            stepper.previousStep();
            expect(stepper.getCurrentStepIndex()).toBe(0);
        });

        test('should jump to specific step', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' },
                { id: 'step3', title: 'Step 3' },
                { id: 'step4', title: 'Step 4' }
            ];

            const stepper = new Stepper(containerElement, steps);

            expect(stepper.goToStep(2)).toBe(true);
            expect(stepper.getCurrentStepIndex()).toBe(2);
        });

        test('should not allow jumping to invalid step index', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' }
            ];

            const stepper = new Stepper(containerElement, steps);

            expect(stepper.goToStep(5)).toBe(false);
            expect(stepper.goToStep(-1)).toBe(false);
        });
    });

    describe('Step State Management', () => {
        test('should track step completion', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' }
            ];

            const stepper = new Stepper(containerElement, steps);

            expect(stepper.getStep(0)?.state.complete).toBe(false);

            stepper.nextStep();
            expect(stepper.getStep(0)?.state.complete).toBe(true);
            expect(stepper.getStep(1)?.state.complete).toBe(false);
        });

        test('should track step validation state', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' }
            ];

            const stepper = new Stepper(containerElement, steps);

            expect(stepper.getStep(0)?.state.valid).toBe(true);

            stepper.markStepInvalid(0, 2);
            expect(stepper.getStep(0)?.state.valid).toBe(false);
            expect(stepper.getStep(0)?.state.errorCount).toBe(2);

            stepper.markStepValid(0);
            expect(stepper.getStep(0)?.state.valid).toBe(true);
        });

        test('should prevent navigation when step is invalid', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' }
            ];

            const stepper = new Stepper(containerElement, steps);

            stepper.markStepInvalid(0);
            expect(stepper.canMoveToNextStep()).toBe(false);
        });

        test('should allow navigation for optional invalid steps', () => {
            const steps = [
                { id: 'step1', title: 'Step 1', optional: true },
                { id: 'step2', title: 'Step 2' }
            ];

            const stepper = new Stepper(containerElement, steps);

            stepper.markStepInvalid(0);
            expect(stepper.canMoveToNextStep()).toBe(true);
        });

        test('should reset all step states', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' },
                { id: 'step3', title: 'Step 3' }
            ];

            const stepper = new Stepper(containerElement, steps);

            stepper.nextStep();
            stepper.nextStep();
            stepper.markStepInvalid(1);

            stepper.reset();

            expect(stepper.getCurrentStepIndex()).toBe(0);
            expect(stepper.getStep(0)?.state.complete).toBe(false);
            expect(stepper.getStep(1)?.state.valid).toBe(true);
        });
    });

    describe('Progress Tracking', () => {
        test('should calculate progress percentage', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' },
                { id: 'step3', title: 'Step 3' },
                { id: 'step4', title: 'Step 4' }
            ];

            const stepper = new Stepper(containerElement, steps);

            expect(stepper.getProgress()).toBe(0);

            stepper.nextStep();
            expect(stepper.getProgress()).toBe(25);

            stepper.nextStep();
            expect(stepper.getProgress()).toBe(50);
        });

        test('should return correct step status', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' },
                { id: 'step3', title: 'Step 3' }
            ];

            const stepper = new Stepper(containerElement, steps);

            expect(stepper.getStepStatus(0)).toBe('current');
            expect(stepper.getStepStatus(1)).toBe('incomplete');
            expect(stepper.getStepStatus(2)).toBe('incomplete');

            stepper.nextStep();

            expect(stepper.getStepStatus(0)).toBe('complete');
            expect(stepper.getStepStatus(1)).toBe('current');
            expect(stepper.getStepStatus(2)).toBe('incomplete');
        });

        test('should mark step with error status', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' }
            ];

            const stepper = new Stepper(containerElement, steps);

            stepper.nextStep();
            stepper.markStepInvalid(1);

            expect(stepper.getStepStatus(1)).toBe('error');
        });
    });

    describe('Step Callbacks', () => {
        test('should trigger step change callback', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' }
            ];

            let oldIndex = -1;
            let newIndex = -1;

            const stepper = new Stepper(containerElement, steps, {
                onStepChange: (old, newIdx) => {
                    oldIndex = old;
                    newIndex = newIdx;
                }
            });

            stepper.nextStep();

            expect(oldIndex).toBe(0);
            expect(newIndex).toBe(1);
        });

        test('should trigger step complete callback', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' }
            ];

            let completedIndex = -1;

            const stepper = new Stepper(containerElement, steps, {
                onStepComplete: (index) => {
                    completedIndex = index;
                }
            });

            stepper.markStepComplete(0);

            expect(completedIndex).toBe(0);
        });

        test('should trigger progress change callback', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' },
                { id: 'step3', title: 'Step 3' }
            ];

            let lastProgress = 0;

            const stepper = new Stepper(containerElement, steps, {
                onProgressChange: (progress) => {
                    lastProgress = progress;
                }
            });

            stepper.markStepComplete(0);

            expect(lastProgress).toBeGreaterThan(0);
        });
    });

    describe('Layout Options', () => {
        test('should support horizontal layout', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' }
            ];

            const stepper = new Stepper(containerElement, steps, {
                layout: 'horizontal'
            });

            expect(stepper).toBeDefined();
        });

        test('should support vertical layout', () => {
            const steps = [
                { id: 'step1', title: 'Step 1' },
                { id: 'step2', title: 'Step 2' }
            ];

            const stepper = new Stepper(containerElement, steps, {
                layout: 'vertical'
            });

            expect(stepper).toBeDefined();
        });
    });
});

describe('Step Navigator', () => {
    let containerElement: HTMLElement;
    let stepper: Stepper;
    let previousBtn: HTMLButtonElement;
    let nextBtn: HTMLButtonElement;
    let submitBtn: HTMLButtonElement;

    beforeEach(() => {
        containerElement = document.createElement('div');
        document.body.appendChild(containerElement);

        const steps = [
            { id: 'step1', title: 'Step 1' },
            { id: 'step2', title: 'Step 2' },
            { id: 'step3', title: 'Step 3' }
        ];

        stepper = new Stepper(containerElement, steps);

        previousBtn = document.createElement('button');
        previousBtn.id = 'prevBtn';
        previousBtn.textContent = 'Previous';
        containerElement.appendChild(previousBtn);

        nextBtn = document.createElement('button');
        nextBtn.id = 'nextBtn';
        nextBtn.textContent = 'Next';
        containerElement.appendChild(nextBtn);

        submitBtn = document.createElement('button');
        submitBtn.id = 'submitBtn';
        submitBtn.textContent = 'Submit';
        containerElement.appendChild(submitBtn);
    });

    afterEach(() => {
        document.body.removeChild(containerElement);
    });

    test('should initialize navigator', () => {
        const navigator = new StepNavigator(stepper, {
            previousButtonSelector: '#prevBtn',
            nextButtonSelector: '#nextBtn',
            submitButtonSelector: '#submitBtn'
        });

        expect(navigator).toBeDefined();
    });

    test('should update button states on navigation', () => {
        const navigator = new StepNavigator(stepper, {
            previousButtonSelector: '#prevBtn',
            nextButtonSelector: '#nextBtn',
            submitButtonSelector: '#submitBtn'
        });

        // At first step, previous should be disabled
        expect(previousBtn.hasAttribute('disabled')).toBe(true);
        expect(nextBtn.hasAttribute('disabled')).toBe(false);

        navigator.goToStep(2);

        // At last step, next should be hidden
        expect(nextBtn.style.display).toBe('none');
        expect(submitBtn.style.display).toBe('');
    });

    test('should handle navigation clicks', (done) => {
        let previousClicked = false;
        let nextClicked = false;

        const navigator = new StepNavigator(stepper, {
            previousButtonSelector: '#prevBtn',
            nextButtonSelector: '#nextBtn',
            submitButtonSelector: '#submitBtn',
            onPrevious: () => {
                previousClicked = true;
            },
            onNext: () => {
                nextClicked = true;
                return true;
            }
        });

        stepper.nextStep();
        nextBtn.click();

        setTimeout(() => {
            stepper.previousStep();
            previousBtn.click();

            setTimeout(() => {
                expect(previousClicked).toBe(true);
                expect(nextClicked).toBe(true);
                done();
            }, 100);
        }, 100);
    });

    test('should validate before navigation', (done) => {
        const navigator = new StepNavigator(stepper, {
            previousButtonSelector: '#prevBtn',
            nextButtonSelector: '#nextBtn',
            submitButtonSelector: '#submitBtn',
            onNext: () => {
                // Simulate validation failure
                return false;
            }
        });

        const initialIndex = stepper.getCurrentStepIndex();
        nextBtn.click();

        setTimeout(() => {
            expect(stepper.getCurrentStepIndex()).toBe(initialIndex);
            done();
        }, 100);
    });
});
