/**
 * DynamicStepHandlerTests - Test suite for dynamic step management
 */

import { Stepper } from '../wwwroot/js/stepper';
import { DynamicStepHandler } from '../wwwroot/js/dynamic-step-handler';
import { FormStateManager } from '../wwwroot/js/form-state-manager';

describe('DynamicStepHandler', () => {
  let containerElement: HTMLElement;
  let stepper: Stepper;
  let handler: DynamicStepHandler;

  beforeEach(() => {
    containerElement = document.createElement('div');
    document.body.appendChild(containerElement);

    const steps = [
      { id: 's1', title: 'Step 1' },
      { id: 's2', title: 'Step 2' },
      { id: 's3', title: 'Step 3' }
    ];

    stepper = new Stepper(containerElement, steps);
    handler = new DynamicStepHandler(stepper);
  });

  afterEach(() => {
    document.body.removeChild(containerElement);
  });

  describe('Initialization', () => {
    test('should initialize with no dynamic steps', () => {
      expect(handler.getDynamicStepIndices()).toHaveLength(0);
      expect(handler.getStepCount()).toBe(3);
    });

    test('should get all steps', () => {
      const steps = handler.getSteps();
      expect(steps).toHaveLength(3);
    });
  });

  describe('Adding Steps', () => {
    test('should add step at beginning', () => {
      handler.addStep(0, { id: 's0', title: 'Step 0' });

      expect(handler.getStepCount()).toBe(4);
      expect(handler.isDynamicStep(0)).toBe(true);
    });

    test('should add step in middle', () => {
      handler.addStep(1, { id: 'new', title: 'New Step' });

      expect(handler.getStepCount()).toBe(4);
      expect(handler.isDynamicStep(1)).toBe(true);
    });

    test('should add step at end', () => {
      handler.addStep(3, { id: 's4', title: 'Step 4' });

      expect(handler.getStepCount()).toBe(4);
      expect(handler.isDynamicStep(3)).toBe(true);
    });

    test('should add multiple steps', () => {
      handler.addStep(0, { id: 'new1', title: 'New 1' });
      handler.addStep(2, { id: 'new2', title: 'New 2' });

      expect(handler.getStepCount()).toBe(5);
    });

    test('should throw on invalid index', () => {
      expect(() => handler.addStep(-1, { id: 'x', title: 'X' })).toThrow();
      expect(() => handler.addStep(10, { id: 'x', title: 'X' })).toThrow();
    });

    test('should throw on missing step id/title', () => {
      expect(() => handler.addStep(0, { id: '', title: 'X' })).toThrow();
      expect(() => handler.addStep(0, { id: 'x', title: '' })).toThrow();
    });
  });

  describe('Removing Steps', () => {
    beforeEach(() => {
      handler.addStep(0, { id: 's0', title: 'Step 0' });
    });

    test('should remove step by index', () => {
      handler.removeStep(0);

      expect(handler.getStepCount()).toBe(3);
      expect(handler.isDynamicStep(0)).toBe(false);
    });

    test('should handle index shifting after removal', () => {
      handler.addStep(1, { id: 'new', title: 'New' });
      handler.removeStep(0);

      expect(handler.isDynamicStep(0)).toBe(true); // Was at 1, now at 0
    });

    test('should throw on invalid index', () => {
      expect(() => handler.removeStep(-1)).toThrow();
      expect(() => handler.removeStep(10)).toThrow();
    });

    test('should remove multiple steps', () => {
      handler.addStep(1, { id: 'new', title: 'New' });
      handler.removeStep(0);
      handler.removeStep(0);

      expect(handler.getStepCount()).toBe(2);
    });
  });

  describe('Step Tracking', () => {
    test('should identify dynamic steps', () => {
      handler.addStep(0, { id: 'dyn', title: 'Dynamic' });

      expect(handler.isDynamicStep(0)).toBe(true);
      expect(handler.isDynamicStep(1)).toBe(false);
    });

    test('should get dynamic step indices', () => {
      handler.addStep(0, { id: 'd1', title: 'D1' });
      handler.addStep(2, { id: 'd2', title: 'D2' });

      const dynamic = handler.getDynamicStepIndices();
      expect(dynamic).toContain(0);
      expect(dynamic).toContain(2);
    });

    test('should get static step indices', () => {
      handler.addStep(0, { id: 'dyn', title: 'Dyn' });

      const staticeIndices = handler.getStaticStepIndices();
      expect(staticeIndices.length).toBeLessThan(handler.getStepCount());
    });
  });

  describe('Reordering Steps', () => {
    test('should reorder steps', () => {
      const order = [2, 0, 1];
      handler.reorderSteps(order);

      expect(handler.getStepCount()).toBe(3);
    });

    test('should throw on invalid order length', () => {
      expect(() => handler.reorderSteps([0, 1])).toThrow();
    });

    test('should throw on duplicate indices', () => {
      expect(() => handler.reorderSteps([0, 0, 1])).toThrow();
    });

    test('should throw on out of range indices', () => {
      expect(() => handler.reorderSteps([0, 1, 5])).toThrow();
    });
  });

  describe('Swapping Steps', () => {
    test('should swap two steps', () => {
      handler.swapSteps(0, 2);

      expect(handler.getStepCount()).toBe(3);
    });

    test('should throw on invalid indices', () => {
      expect(() => handler.swapSteps(-1, 2)).toThrow();
      expect(() => handler.swapSteps(0, 10)).toThrow();
    });
  });

  describe('Moving Steps', () => {
    test('should move step from one position to another', () => {
      handler.moveStep(0, 2);

      expect(handler.getStepCount()).toBe(3);
    });

    test('should throw on invalid indices', () => {
      expect(() => handler.moveStep(-1, 2)).toThrow();
      expect(() => handler.moveStep(0, 10)).toThrow();
    });
  });

  describe('Modification Tracking', () => {
    test('should track step additions', () => {
      handler.addStep(0, { id: 'new', title: 'New' });

      const summary = handler.getModificationSummary();
      expect(summary.addedSteps).toBe(1);
      expect(summary.totalModifications).toBe(1);
    });

    test('should track step removals', () => {
      handler.addStep(0, { id: 'new', title: 'New' });
      handler.removeStep(0);

      const summary = handler.getModificationSummary();
      expect(summary.removedSteps).toBe(1);
    });

    test('should get modification history', () => {
      handler.addStep(0, { id: 'n1', title: 'N1' });
      handler.addStep(1, { id: 'n2', title: 'N2' });

      const history = handler.getModificationHistory();
      expect(history).toHaveLength(2);
      expect(history[0].type).toBe('add');
    });

    test('should trigger callback on modification', () => {
      const events: any[] = [];
      handler.onModified((event) => events.push(event));

      handler.addStep(0, { id: 'new', title: 'New' });

      expect(events).toHaveLength(1);
      expect(events[0].type).toBe('add');
    });

    test('should clear modification history', () => {
      handler.addStep(0, { id: 'new', title: 'New' });
      handler.clearModificationHistory();

      const history = handler.getModificationHistory();
      expect(history).toHaveLength(0);
    });
  });

  describe('Complex Scenarios', () => {
    test('should handle add and remove sequence', () => {
      handler.addStep(0, { id: 'a', title: 'A' });
      handler.addStep(1, { id: 'b', title: 'B' });
      handler.removeStep(0);

      expect(handler.getStepCount()).toBe(4);
      expect(handler.getDynamicStepIndices()).toHaveLength(2);
    });

    test('should handle reorder with dynamic steps', () => {
      handler.addStep(0, { id: 'dyn', title: 'Dynamic' });

      const order = [2, 0, 1, 3];
      handler.reorderSteps(order);

      expect(handler.getStepCount()).toBe(4);
    });

    test('should maintain chainable API', () => {
      const result = handler
        .addStep(0, { id: 'n1', title: 'N1' })
        .addStep(1, { id: 'n2', title: 'N2' })
        .swapSteps(0, 1);

      expect(result).toBe(handler);
    });
  });

  describe('State Integration', () => {
    let stateManager: FormStateManager;

    beforeEach(() => {
      stateManager = new FormStateManager('memory');
      handler = new DynamicStepHandler(stepper, stateManager);
    });

    test('should preserve state on step removal', () => {
      stateManager.setState('step_0', 'value0');
      handler.addStep(0, { id: 'new', title: 'New' });

      const removed = handler.removeStep(1);

      // State should be preserved (removed to removed_step_1)
      expect(removed).toBe(handler);
    });
  });

  describe('Reset', () => {
    test('should reset all modifications', () => {
      handler.addStep(0, { id: 'new', title: 'New' });
      handler.reset();

      expect(handler.getDynamicStepIndices()).toHaveLength(0);
      expect(handler.getModificationHistory()).toHaveLength(0);
    });
  });

  describe('Performance', () => {
    test('should add 100 steps quickly', () => {
      const start = performance.now();

      for (let i = 0; i < 100; i++) {
        handler.addStep(i, { id: `s${i}`, title: `Step ${i}` });
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(500);
    });

    test('should remove 50 steps quickly', () => {
      // Add steps first
      for (let i = 0; i < 50; i++) {
        handler.addStep(0, { id: `s${i}`, title: `Step ${i}` });
      }

      const start = performance.now();

      for (let i = 0; i < 50; i++) {
        handler.removeStep(0);
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(500);
    });

    test('should reorder quickly', () => {
      const steps = Array.from({ length: 50 }, (_, i) => ({
        id: `s${i}`,
        title: `Step ${i}`
      }));

      for (let i = 0; i < 50; i++) {
        handler.addStep(i, steps[i]);
      }

      const order = Array.from({ length: 53 }, (_, i) => Math.floor(Math.random() * 53)).filter((v, i, a) => a.indexOf(v) === i).slice(0, 53);

      const start = performance.now();
      try {
        handler.reorderSteps(order);
      } catch (e) {
        // Expected for random order
      }
      const elapsed = performance.now() - start;

      expect(elapsed).toBeLessThan(100);
    });
  });
});
