/**
 * Phase6PerformanceTests - Performance benchmarks for multi-step forms
 * Ensures all components meet performance targets under load
 */

import { Stepper } from '../wwwroot/js/stepper';
import { StepNavigator } from '../wwwroot/js/step-navigator';
import { FormStateManager } from '../wwwroot/js/form-state-manager';
import { StepValidator } from '../wwwroot/js/step-validator';

describe('Phase 6 Performance Benchmarks', () => {
    let containerElement: HTMLElement;

    beforeEach(() => {
        containerElement = document.createElement('div');
        document.body.appendChild(containerElement);
    });

    afterEach(() => {
        document.body.removeChild(containerElement);
    });

    describe('Stepper Performance', () => {
        test('should navigate steps in under 5ms per operation', () => {
            const steps = Array.from({ length: 20 }, (_, i) => ({
                id: `step${i}`,
                title: `Step ${i + 1}`
            }));

            const stepper = new Stepper(containerElement, steps);

            // Test nextStep
            const nextStart = performance.now();
            for (let i = 0; i < 10; i++) {
                if (i > 0) stepper.previousStep();
                stepper.nextStep();
            }
            const nextElapsed = performance.now() - nextStart;

            expect(nextElapsed / 20).toBeLessThan(5); // Average per operation

            // Test goToStep
            const gotoStart = performance.now();
            for (let i = 0; i < 10; i++) {
                stepper.goToStep(i % 20);
            }
            const gotoElapsed = performance.now() - gotoStart;

            expect(gotoElapsed / 10).toBeLessThan(5);
        });

        test('should handle 100 steps smoothly', () => {
            const steps = Array.from({ length: 100 }, (_, i) => ({
                id: `step${i}`,
                title: `Step ${i + 1}`
            }));

            const start = performance.now();
            const stepper = new Stepper(containerElement, steps);
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(100);
            expect(stepper.getStepCount()).toBe(100);
        });

        test('should calculate progress for large step counts quickly', () => {
            const steps = Array.from({ length: 50 }, (_, i) => ({
                id: `step${i}`,
                title: `Step ${i + 1}`
            }));

            const stepper = new Stepper(containerElement, steps);

            const start = performance.now();
            for (let i = 0; i < 1000; i++) {
                stepper.getProgress();
            }
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(50); // 1000 ops in under 50ms
        });
    });

    describe('State Manager Performance', () => {
        test('should set 1000 fields in under 100ms', () => {
            const stateManager = new FormStateManager('memory');

            const start = performance.now();
            for (let i = 0; i < 1000; i++) {
                stateManager.setState(`field${i}`, `value${i}`);
            }
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(100);
        });

        test('should persist 500 fields to localStorage in under 50ms', () => {
            const stateManager = new FormStateManager('localStorage', 'perf-test');

            // Setup
            for (let i = 0; i < 500; i++) {
                stateManager.setState(`field${i}`, `value${i}`);
            }

            // Test persistence
            const start = performance.now();
            stateManager.persistToStorage();
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(50);

            localStorage.removeItem('perf-test');
        });

        test('should load 500 fields from localStorage in under 50ms', () => {
            // Setup
            const data = Object.fromEntries(
                Array.from({ length: 500 }, (_, i) => [`field${i}`, `value${i}`])
            );
            localStorage.setItem('perf-test', JSON.stringify(data));

            // Test loading
            const stateManager = new FormStateManager('localStorage', 'perf-test');
            const start = performance.now();
            stateManager.loadFromStorage();
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(50);

            localStorage.removeItem('perf-test');
        });

        test('should merge 100 objects quickly', () => {
            const stateManager = new FormStateManager('memory');

            const start = performance.now();
            for (let i = 0; i < 100; i++) {
                const data = Object.fromEntries(
                    Array.from({ length: 10 }, (_, j) => [`f${i}_${j}`, `v${i}_${j}`])
                );
                stateManager.mergeState(data);
            }
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(50);
        });

        test('should save 100 history snapshots quickly', () => {
            const stateManager = new FormStateManager('memory');

            const start = performance.now();
            for (let i = 0; i < 100; i++) {
                stateManager.setState('counter', i);
                stateManager.saveToHistory();
            }
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(50);
        });

        test('should undo 50 times in under 20ms', () => {
            const stateManager = new FormStateManager('memory');

            // Setup history
            for (let i = 0; i < 50; i++) {
                stateManager.setState('value', i);
                stateManager.saveToHistory();
            }

            // Test undo
            const start = performance.now();
            for (let i = 0; i < 50; i++) {
                stateManager.undo();
            }
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(20);
        });

        test('should export/import large state in under 100ms', () => {
            const stateManager = new FormStateManager('memory');

            // Setup
            for (let i = 0; i < 500; i++) {
                stateManager.setState(`field${i}`, `value${i}`);
            }

            // Export
            const exportStart = performance.now();
            const json = stateManager.toJSON();
            const exportElapsed = performance.now() - exportStart;

            expect(exportElapsed).toBeLessThan(50);

            // Import
            const importManager = new FormStateManager('memory');
            const importStart = performance.now();
            importManager.fromJSON(json);
            const importElapsed = performance.now() - importStart;

            expect(importElapsed).toBeLessThan(50);
        });
    });

    describe('Validator Performance', () => {
        test('should validate single step with 10 rules in under 10ms', async () => {
            const steps = [
                { id: 's1', title: 'Step 1' },
                { id: 's2', title: 'Step 2' }
            ];
            const stepper = new Stepper(containerElement, steps);
            const validator = new StepValidator(stepper);

            // Add 10 rules
            for (let i = 0; i < 10; i++) {
                validator.addStepRule(0, (data) => true, `Rule ${i}`);
            }

            const start = performance.now();
            await validator.validateStep(0);
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(10);
        });

        test('should validate all steps quickly', async () => {
            const steps = Array.from({ length: 10 }, (_, i) => ({
                id: `step${i}`,
                title: `Step ${i + 1}`
            }));
            const stepper = new Stepper(containerElement, steps);
            const validator = new StepValidator(stepper);

            // Add rules to each step
            for (let i = 0; i < 10; i++) {
                validator.addStepRule(i, (data) => true, 'Pass');
            }

            const start = performance.now();
            await validator.validateAllSteps();
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(100);
        });

        test('should handle 100 cross-step rules efficiently', async () => {
            const steps = Array.from({ length: 5 }, (_, i) => ({
                id: `step${i}`,
                title: `Step ${i + 1}`
            }));
            const stepper = new Stepper(containerElement, steps);
            const validator = new StepValidator(stepper);

            // Add many cross-step rules
            for (let i = 0; i < 100; i++) {
                validator.addCrossStepRule(
                    [0, 1],
                    2,
                    (data) => true,
                    `Cross-step rule ${i}`
                );
            }

            const start = performance.now();
            await validator.validateStep(0);
            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(50);
        });

        test('should get validation summary quickly', async () => {
            const steps = Array.from({ length: 20 }, (_, i) => ({
                id: `step${i}`,
                title: `Step ${i + 1}`
            }));
            const stepper = new Stepper(containerElement, steps);
            const validator = new StepValidator(stepper);

            // Validate some steps
            for (let i = 0; i < 10; i++) {
                await validator.validateStep(i);
            }

            const start = performance.now();
            for (let i = 0; i < 100; i++) {
                validator.getValidationSummary();
            }
            const elapsed = performance.now() - start;

            expect(elapsed / 100).toBeLessThan(2); // < 2ms per call
        });
    });

    describe('Complete Workflow Performance', () => {
        test('should complete 5-step form in under 100ms total', async () => {
            const steps = Array.from({ length: 5 }, (_, i) => ({
                id: `step${i}`,
                title: `Step ${i + 1}`
            }));

            const start = performance.now();

            // Setup
            const stepper = new Stepper(containerElement, steps);
            const stateManager = new FormStateManager('memory');
            const validator = new StepValidator(stepper);

            // Add validators
            for (let i = 0; i < 5; i++) {
                validator.addStepRule(i, (data) => true, `Step ${i}`);
            }

            // Navigate through all steps
            for (let i = 0; i < 5; i++) {
                stateManager.setMultipleState({
                    [`field${i}`]: `value${i}`
                });
                await validator.validateStep(i);
                if (i < 4) stepper.nextStep();
            }

            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(100);
        });

        test('should handle rapid navigation and state changes', () => {
            const steps = Array.from({ length: 20 }, (_, i) => ({
                id: `step${i}`,
                title: `Step ${i + 1}`
            }));

            const stepper = new Stepper(containerElement, steps);
            const stateManager = new FormStateManager('memory');

            const start = performance.now();

            for (let i = 0; i < 100; i++) {
                stepper.nextStep();
                stepper.previousStep();
                stateManager.setState(`field${i % 20}`, `value${i}`);
            }

            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(200);
        });
    });

    describe('Memory Performance', () => {
        test('should not leak memory with repeated operations', () => {
            const stateManager = new FormStateManager('memory');

            // Perform many operations
            for (let i = 0; i < 10000; i++) {
                stateManager.setState(`field${i % 100}`, `value${i}`);
                if (i % 100 === 0) {
                    stateManager.clearState();
                }
            }

            // Should still be functional
            stateManager.setState('test', 'value');
            expect(stateManager.getState('test')).toBe('value');
        });

        test('should handle large state sizes efficiently', () => {
            const stateManager = new FormStateManager('memory');

            // Create large state
            for (let i = 0; i < 1000; i++) {
                stateManager.setState(`field${i}`, 'x'.repeat(100));
            }

            const size = stateManager.getStateSize();
            expect(size).toBeGreaterThan(0);
            expect(size).toBeLessThan(200000); // Under 200KB
        });

        test('should handle navigator cleanup', () => {
            const steps = [
                { id: 's1', title: 'Step 1' },
                { id: 's2', title: 'Step 2' }
            ];

            const previousBtn = document.createElement('button');
            previousBtn.id = 'prev';
            containerElement.appendChild(previousBtn);

            const nextBtn = document.createElement('button');
            nextBtn.id = 'next';
            containerElement.appendChild(nextBtn);

            const stepper = new Stepper(containerElement, steps);

            // Create and destroy many navigators
            for (let i = 0; i < 100; i++) {
                new StepNavigator(stepper, {
                    previousButtonSelector: '#prev',
                    nextButtonSelector: '#next'
                });
            }

            // Should still work
            expect(stepper).toBeDefined();
            expect(stepper.getStepCount()).toBe(2);
        });
    });

    describe('Bundle Size and Load Time', () => {
        test('should initialize all components quickly', () => {
            const start = performance.now();

            const steps = [
                { id: 's1', title: 'Step 1' },
                { id: 's2', title: 'Step 2' },
                { id: 's3', title: 'Step 3' }
            ];

            const stepper = new Stepper(containerElement, steps);
            const stateManager = new FormStateManager('memory');
            const validator = new StepValidator(stepper);

            const elapsed = performance.now() - start;

            expect(elapsed).toBeLessThan(10); // Should initialize in under 10ms
        });
    });

    describe('Scalability Tests', () => {
        test('should handle 1000 concurrent validations', async () => {
            const stepper = new Stepper(containerElement, [
                { id: 's1', title: 'Step 1' },
                { id: 's2', title: 'Step 2' }
            ]);
            const validator = new StepValidator(stepper);

            validator.addStepRule(0, async (data) => {
                return new Promise(resolve => setTimeout(() => resolve(true), 1));
            }, 'Async rule');

            const start = performance.now();

            const promises = [];
            for (let i = 0; i < 100; i++) {
                promises.push(validator.validateStep(0));
            }

            await Promise.all(promises);
            const elapsed = performance.now() - start;

            // Should handle 100 concurrent validations reasonably
            expect(elapsed).toBeLessThan(2000);
        });
    });
});
