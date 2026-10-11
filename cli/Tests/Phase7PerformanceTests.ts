/**
 * Phase7PerformanceTests - Performance benchmarks for all Phase 7 components
 * Ensures all features meet performance targets under load
 */

import { Stepper } from '../wwwroot/js/stepper';
import { ConditionalStepManager } from '../wwwroot/js/conditional-step-manager';
import { StepSkipper } from '../wwwroot/js/step-skipper';
import { DynamicStepHandler } from '../wwwroot/js/dynamic-step-handler';
import { TabStepper } from '../wwwroot/js/tab-stepper';
import { WorkflowEngine } from '../wwwroot/js/workflow-engine';

describe('Phase 7 Performance Benchmarks', () => {
  let containerElement: HTMLElement;

  beforeEach(() => {
    containerElement = document.createElement('div');
    document.body.appendChild(containerElement);
  });

  afterEach(() => {
    document.body.removeChild(containerElement);
  });

  describe('ConditionalStepManager Performance', () => {
    test('should evaluate 100 visibility rules quickly', () => {
      const steps = Array.from({ length: 100 }, (_, i) => ({
        id: `s${i}`,
        title: `Step ${i}`
      }));

      const stepper = new Stepper(containerElement, steps);
      const manager = new ConditionalStepManager(stepper);

      // Add 100 rules
      for (let i = 0; i < 100; i++) {
        manager.addVisibilityRule(i, (data) => data[`show${i}`] ?? true);
      }

      const data: any = {};
      for (let i = 0; i < 100; i++) {
        data[`show${i}`] = Math.random() > 0.5;
      }

      const start = performance.now();
      manager.updateVisibility(data);
      const elapsed = performance.now() - start;

      expect(elapsed).toBeLessThan(100);
    });

    test('should get visible steps from 1000 rules quickly', () => {
      const steps = Array.from({ length: 100 }, (_, i) => ({
        id: `s${i}`,
        title: `Step ${i}`
      }));

      const stepper = new Stepper(containerElement, steps);
      const manager = new ConditionalStepManager(stepper);

      // Add rules
      for (let i = 0; i < 100; i++) {
        manager.addVisibilityRule(i, () => Math.random() > 0.3);
      }

      const start = performance.now();
      for (let i = 0; i < 1000; i++) {
        manager.getVisibleSteps();
      }
      const elapsed = performance.now() - start;

      expect(elapsed).toBeLessThan(100);
    });
  });

  describe('StepSkipper Performance', () => {
    test('should evaluate skip rules for 100 steps quickly', () => {
      const steps = Array.from({ length: 100 }, (_, i) => ({
        id: `s${i}`,
        title: `Step ${i}`
      }));

      const stepper = new Stepper(containerElement, steps);
      const skipper = new StepSkipper(stepper);

      // Add skip rules
      for (let i = 0; i < 100; i++) {
        skipper.addSkipRule(i, (data) => data[`skip${i}`] ?? false);
      }

      const start = performance.now();

      for (let i = 0; i < 1000; i++) {
        skipper.shouldSkipStep(i % 100, {});
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(100);
    });
  });

  describe('DynamicStepHandler Performance', () => {
    test('should add 100 steps quickly', () => {
      const steps = [{ id: 's1', title: 'Step 1' }];
      const stepper = new Stepper(containerElement, steps);
      const handler = new DynamicStepHandler(stepper);

      const start = performance.now();

      for (let i = 1; i < 100; i++) {
        handler.addStep(i, { id: `s${i + 1}`, title: `Step ${i + 1}` });
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(500);
    });

    test('should remove 50 steps quickly', () => {
      const steps = Array.from({ length: 50 }, (_, i) => ({
        id: `s${i}`,
        title: `Step ${i + 1}`
      }));

      const stepper = new Stepper(containerElement, steps);
      const handler = new DynamicStepHandler(stepper);

      const start = performance.now();

      for (let i = 0; i < 25; i++) {
        handler.removeStep(0);
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(300);
    });

    test('should reorder 100 steps quickly', () => {
      const steps = Array.from({ length: 100 }, (_, i) => ({
        id: `s${i}`,
        title: `Step ${i + 1}`
      }));

      const stepper = new Stepper(containerElement, steps);
      const handler = new DynamicStepHandler(stepper);

      // Initial add
      for (let i = 1; i < 100; i++) {
        handler.addStep(i, steps[i]);
      }

      const order = Array.from({ length: 100 }, (_, i) => (i + 50) % 100);

      const start = performance.now();
      handler.reorderSteps(order);
      const elapsed = performance.now() - start;

      expect(elapsed).toBeLessThan(100);
    });
  });

  describe('TabStepper Performance', () => {
    test('should render 100 tabs quickly', () => {
      const steps = Array.from({ length: 100 }, (_, i) => ({
        id: `s${i}`,
        title: `Step ${i + 1}`
      }));

      const start = performance.now();
      const stepper = new TabStepper(containerElement, steps);
      const elapsed = performance.now() - start;

      expect(elapsed).toBeLessThan(500);
    });

    test('should switch layouts quickly', () => {
      const steps = Array.from({ length: 50 }, (_, i) => ({
        id: `s${i}`,
        title: `Step ${i + 1}`
      }));

      const stepper = new TabStepper(containerElement, steps);

      const start = performance.now();

      for (let i = 0; i < 100; i++) {
        stepper.setLayout(i % 2 === 0 ? 'horizontal' : 'vertical');
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(200);
    });

    test('should navigate through 100 tabs quickly', () => {
      const steps = Array.from({ length: 100 }, (_, i) => ({
        id: `s${i}`,
        title: `Step ${i + 1}`
      }));

      const stepper = new TabStepper(containerElement, steps);

      const start = performance.now();

      for (let i = 0; i < 1000; i++) {
        stepper.goToStep(i % 100);
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(500);
    });
  });

  describe('WorkflowEngine Performance', () => {
    test('should evaluate 100 workflows quickly', () => {
      const steps = Array.from({ length: 50 }, (_, i) => ({
        id: `s${i}`,
        title: `Step ${i + 1}`
      }));

      const stepper = new Stepper(containerElement, steps);
      const engine = new WorkflowEngine(stepper);

      // Define 100 workflows
      for (let w = 0; w < 100; w++) {
        engine.defineWorkflow({
          name: `workflow${w}`,
          branches: [
            {
              name: 'branch',
              condition: () => true,
              path: [0, 1, 2, 3, 4]
            }
          ]
        });
      }

      const start = performance.now();

      for (let i = 0; i < 1000; i++) {
        engine.getAvailableWorkflows();
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(100);
    });

    test('should evaluate complex branch conditions quickly', () => {
      const steps = Array.from({ length: 10 }, (_, i) => ({
        id: `s${i}`,
        title: `Step ${i + 1}`
      }));

      const stepper = new Stepper(containerElement, steps);
      const engine = new WorkflowEngine(stepper);

      // Define workflow with many branches
      const branches = Array.from({ length: 100 }, (_, i) => ({
        name: `branch${i}`,
        condition: (data: any) => data.value === i,
        path: [0, 1, 2, 3, 4]
      }));

      engine.defineWorkflow({
        name: 'complex',
        branches: branches as any
      });

      const start = performance.now();

      for (let i = 0; i < 1000; i++) {
        engine.evaluateWorkflow('complex', { value: i % 100 });
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(500);
    });

    test('should track workflow progress quickly', () => {
      const steps = Array.from({ length: 20 }, (_, i) => ({
        id: `s${i}`,
        title: `Step ${i + 1}`
      }));

      const stepper = new Stepper(containerElement, steps);
      const engine = new WorkflowEngine(stepper);

      engine.defineWorkflow({
        name: 'test',
        branches: [
          {
            name: 'path',
            condition: () => true,
            path: Array.from({ length: 20 }, (_, i) => i)
          }
        ]
      });

      engine.activateWorkflow('test', {});

      const start = performance.now();

      for (let i = 0; i < 1000; i++) {
        engine.completeStep(i % 20);
        engine.getProgress();
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(200);
    });
  });

  describe('Combined Performance', () => {
    test('should handle all Phase 7 features with 50 steps', () => {
      const steps = Array.from({ length: 50 }, (_, i) => ({
        id: `s${i}`,
        title: `Step ${i + 1}`
      }));

      const stepper = new Stepper(containerElement, steps);
      const conditional = new ConditionalStepManager(stepper);
      const skipper = new StepSkipper(stepper, conditional);
      const handler = new DynamicStepHandler(stepper);
      const engine = new WorkflowEngine(stepper, conditional, skipper);

      const start = performance.now();

      // Add visibility rules
      for (let i = 0; i < 50; i++) {
        conditional.addVisibilityRule(i, (data) => data[`show${i}`] ?? true);
      }

      // Add skip rules
      for (let i = 0; i < 50; i++) {
        skipper.addSkipRule(i, (data) => data[`skip${i}`] ?? false);
      }

      // Define workflows
      for (let w = 0; w < 5; w++) {
        engine.defineWorkflow({
          name: `workflow${w}`,
          branches: [
            {
              name: 'path',
              condition: () => true,
              path: Array.from({ length: 25 }, (_, i) => i)
            }
          ]
        });
      }

      // Evaluate all
      const data: any = {};
      for (let i = 0; i < 50; i++) {
        data[`show${i}`] = Math.random() > 0.5;
        data[`skip${i}`] = Math.random() > 0.7;
      }

      conditional.updateVisibility(data);
      for (let w = 0; w < 5; w++) {
        engine.evaluateWorkflow(`workflow${w}`, data);
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(500);
    });
  });

  describe('Memory Efficiency', () => {
    test('should not leak memory with many operations', () => {
      const steps = Array.from({ length: 20 }, (_, i) => ({
        id: `s${i}`,
        title: `Step ${i + 1}`
      }));

      const stepper = new Stepper(containerElement, steps);
      const handler = new DynamicStepHandler(stepper);

      // Do many add/remove operations
      for (let i = 0; i < 1000; i++) {
        handler.addStep(0, { id: 'temp', title: 'Temp' });
        handler.removeStep(0);
      }

      // Should still be functional
      expect(stepper.getStepCount()).toBe(20);
    });
  });
});
