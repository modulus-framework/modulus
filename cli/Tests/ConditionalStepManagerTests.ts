/**
 * ConditionalStepManagerTests - Test suite for conditional step visibility
 */

import { Stepper } from '../wwwroot/js/stepper';
import { ConditionalStepManager } from '../wwwroot/js/conditional-step-manager';

describe('ConditionalStepManager', () => {
  let containerElement: HTMLElement;
  let stepper: Stepper;
  let manager: ConditionalStepManager;

  beforeEach(() => {
    containerElement = document.createElement('div');
    document.body.appendChild(containerElement);

    const steps = [
      { id: 's1', title: 'Step 1' },
      { id: 's2', title: 'Step 2' },
      { id: 's3', title: 'Step 3' },
      { id: 's4', title: 'Step 4' },
      { id: 's5', title: 'Step 5' }
    ];

    stepper = new Stepper(containerElement, steps);
    manager = new ConditionalStepManager(stepper);
  });

  afterEach(() => {
    document.body.removeChild(containerElement);
  });

  describe('Initialization', () => {
    test('should initialize with all steps visible', () => {
      expect(manager.isStepVisible(0)).toBe(true);
      expect(manager.isStepVisible(1)).toBe(true);
      expect(manager.isStepVisible(4)).toBe(true);
    });

    test('should get all visible steps', () => {
      const visible = manager.getVisibleSteps();
      expect(visible).toHaveLength(5);
      expect(visible).toEqual([0, 1, 2, 3, 4]);
    });

    test('should get correct visible step count', () => {
      expect(manager.getVisibleStepCount()).toBe(5);
    });
  });

  describe('Visibility Rules', () => {
    test('should add visibility rule', () => {
      manager.addVisibilityRule(1, (data) => data.showStep2);
      expect(manager.getVisibilityRules().has(1)).toBe(true);
    });

    test('should evaluate visibility rule correctly', () => {
      manager.addVisibilityRule(1, (data) => data.showStep2);

      manager.updateVisibility({ showStep2: true });
      expect(manager.isStepVisible(1)).toBe(true);

      manager.updateVisibility({ showStep2: false });
      expect(manager.isStepVisible(1)).toBe(false);
    });

    test('should handle multiple visibility rules', () => {
      manager.addVisibilityRule(1, (data) => data.show2);
      manager.addVisibilityRule(2, (data) => data.show3);
      manager.addVisibilityRule(3, (data) => data.show4);

      const visible = manager.updateVisibility({
        show2: true,
        show3: false,
        show4: true
      });

      expect(visible).toHaveLength(3);
      expect(manager.isStepVisible(1)).toBe(true);
      expect(manager.isStepVisible(2)).toBe(false);
      expect(manager.isStepVisible(3)).toBe(true);
    });

    test('should remove visibility rule', () => {
      manager.addVisibilityRule(1, (data) => false);
      manager.removeVisibilityRule(1);

      expect(manager.isStepVisible(1)).toBe(true);
    });
  });

  describe('Visibility Changes', () => {
    test('should track visibility changes', () => {
      manager.addVisibilityRule(1, (data) => data.showStep2);

      const changes = manager.updateVisibility({ showStep2: false });

      expect(changes).toHaveLength(1);
      expect(changes[0].stepIndex).toBe(1);
      expect(changes[0].wasVisible).toBe(true);
      expect(changes[0].isVisible).toBe(false);
    });

    test('should not report changes for unchanged visibility', () => {
      manager.addVisibilityRule(1, (data) => true);

      const changes1 = manager.updateVisibility({ data: 'test' });
      expect(changes1).toHaveLength(0);
    });

    test('should trigger callback on visibility changes', () => {
      const changes: any[] = [];
      manager.onVisibilityChanged((c) => changes.push(...c));
      manager.addVisibilityRule(1, (data) => data.show);

      manager.updateVisibility({ show: false });

      expect(changes).toHaveLength(1);
      expect(changes[0].isVisible).toBe(false);
    });
  });

  describe('Visible Steps List', () => {
    test('should return only visible steps', () => {
      manager.addVisibilityRule(1, (data) => false);
      manager.addVisibilityRule(3, (data) => false);

      manager.updateVisibility({});

      const visible = manager.getVisibleSteps();
      expect(visible).toEqual([0, 2, 4]);
    });

    test('should update visible list on visibility changes', () => {
      manager.addVisibilityRule(1, (data) => data.show2);

      manager.updateVisibility({ show2: true });
      expect(manager.getVisibleSteps()).toContain(1);

      manager.updateVisibility({ show2: false });
      expect(manager.getVisibleSteps()).not.toContain(1);
    });
  });

  describe('Step Navigation', () => {
    test('should get next visible step', () => {
      manager.addVisibilityRule(1, (data) => false);
      manager.updateVisibility({});

      const next = manager.getNextVisibleStep(0);
      expect(next).toBe(2); // Skip hidden step 1
    });

    test('should get previous visible step', () => {
      manager.addVisibilityRule(1, (data) => false);
      manager.updateVisibility({});

      const prev = manager.getPreviousVisibleStep(2);
      expect(prev).toBe(0); // Skip hidden step 1
    });

    test('should return null when no next visible step', () => {
      manager.addVisibilityRule(3, (data) => false);
      manager.addVisibilityRule(4, (data) => false);
      manager.updateVisibility({});

      const next = manager.getNextVisibleStep(2);
      expect(next).toBeNull();
    });

    test('should return null when no previous visible step', () => {
      manager.addVisibilityRule(0, (data) => false);
      manager.updateVisibility({});

      const prev = manager.getPreviousVisibleStep(1);
      expect(prev).toBeNull();
    });
  });

  describe('Dependencies', () => {
    test('should track step dependencies', () => {
      manager.addVisibilityRule(2, (data) => data.show, [0, 1]);

      expect(manager.hasDependencies(2)).toBe(true);
      const deps = manager.getDependencies(2);
      expect(deps.has(0)).toBe(true);
      expect(deps.has(1)).toBe(true);
    });

    test('should find dependent steps', () => {
      manager.addVisibilityRule(2, (data) => true, [0]);
      manager.addVisibilityRule(3, (data) => true, [0, 1]);

      const dependents = manager.getDependents(0);
      expect(dependents).toContain(2);
      expect(dependents).toContain(3);
    });

    test('should handle steps with no dependencies', () => {
      expect(manager.hasDependencies(0)).toBe(false);
      expect(manager.getDependencies(0).size).toBe(0);
    });
  });

  describe('Error Handling', () => {
    test('should throw on invalid step index', () => {
      expect(() => manager.addVisibilityRule(-1, () => true)).toThrow();
      expect(() => manager.addVisibilityRule(5, () => true)).toThrow();
    });

    test('should handle rule evaluation errors gracefully', () => {
      manager.addVisibilityRule(1, () => {
        throw new Error('Rule error');
      });

      expect(() => manager.updateVisibility({})).not.toThrow();
      expect(manager.isStepVisible(1)).toBe(true); // Falls back to true on error
    });
  });

  describe('State Management', () => {
    test('should reset visibility rules', () => {
      manager.addVisibilityRule(1, (data) => false);
      manager.updateVisibility({});

      expect(manager.isStepVisible(1)).toBe(false);

      manager.reset();

      expect(manager.getVisibilityRules().size).toBe(0);
      expect(manager.isStepVisible(1)).toBe(true);
    });

    test('should export visibility state', () => {
      manager.addVisibilityRule(1, (data) => data.show);
      manager.updateVisibility({ show: false });

      const exported = manager.exportVisibilityState();
      const state = JSON.parse(exported);

      expect(state.rules).toBeDefined();
      expect(state.visibility).toBeDefined();
    });

    test('should provide visibility summary', () => {
      manager.addVisibilityRule(1, (data) => false);
      manager.addVisibilityRule(3, (data) => false);
      manager.updateVisibility({});

      const summary = manager.getVisibilitySummary();

      expect(summary.totalSteps).toBe(5);
      expect(summary.visibleSteps).toBe(3);
      expect(summary.hiddenSteps).toBe(2);
      expect(summary.rulesCount).toBe(2);
    });
  });

  describe('Complex Scenarios', () => {
    test('should handle conditional visibility based on multiple fields', () => {
      manager.addVisibilityRule(2, (data) => {
        return data.country === 'US' && data.showState;
      });

      manager.updateVisibility({ country: 'US', showState: true });
      expect(manager.isStepVisible(2)).toBe(true);

      manager.updateVisibility({ country: 'CA', showState: true });
      expect(manager.isStepVisible(2)).toBe(false);

      manager.updateVisibility({ country: 'US', showState: false });
      expect(manager.isStepVisible(2)).toBe(false);
    });

    test('should handle cascading visibility changes', () => {
      manager.addVisibilityRule(1, (data) => data.step1Visible);
      manager.addVisibilityRule(2, (data) => data.step2Visible);

      const changes = manager.updateVisibility({
        step1Visible: false,
        step2Visible: false
      });

      expect(changes).toHaveLength(2);
      expect(manager.getVisibleStepCount()).toBe(3);
    });

    test('should maintain chainable API', () => {
      const result = manager
        .addVisibilityRule(1, (data) => true)
        .addVisibilityRule(2, (data) => false)
        .removeVisibilityRule(2)
        .reset();

      expect(result).toBe(manager);
    });
  });

  describe('Performance', () => {
    test('should evaluate 100 visibility rules quickly', () => {
      const tempSteps = Array.from({ length: 100 }, (_, i) => ({
        id: `s${i}`,
        title: `Step ${i + 1}`
      }));

      const tempStepper = new Stepper(containerElement, tempSteps);
      const tempManager = new ConditionalStepManager(tempStepper);

      const start = performance.now();

      for (let i = 0; i < 100; i++) {
        tempManager.addVisibilityRule(i, (data) => data[`show${i}`] ?? true);
      }

      const updateStart = performance.now();
      const data: any = {};
      for (let i = 0; i < 100; i++) {
        data[`show${i}`] = Math.random() > 0.5;
      }
      tempManager.updateVisibility(data);
      const updateElapsed = performance.now() - updateStart;

      expect(updateElapsed).toBeLessThan(50);
    });

    test('should get visible steps quickly with many rules', () => {
      for (let i = 0; i < 5; i++) {
        manager.addVisibilityRule(i, (data) => Math.random() > 0.5);
      }

      const start = performance.now();
      for (let j = 0; j < 1000; j++) {
        manager.getVisibleSteps();
      }
      const elapsed = performance.now() - start;

      expect(elapsed).toBeLessThan(100);
    });
  });
});
