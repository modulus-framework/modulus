/**
 * StepSkipperTests - Test suite for step skipping functionality
 */

import { Stepper } from '../wwwroot/js/stepper';
import { StepSkipper } from '../wwwroot/js/step-skipper';
import { ConditionalStepManager } from '../wwwroot/js/conditional-step-manager';

describe('StepSkipper', () => {
  let containerElement: HTMLElement;
  let stepper: Stepper;
  let skipper: StepSkipper;

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
    skipper = new StepSkipper(stepper);
  });

  afterEach(() => {
    document.body.removeChild(containerElement);
  });

  describe('Initialization', () => {
    test('should initialize with no skipped steps', () => {
      expect(skipper.getSkippedSteps()).toHaveLength(0);
      expect(skipper.hasSkippedSteps()).toBe(false);
    });

    test('should start with all steps navigable', () => {
      expect(skipper.getNavigableStepCount()).toBe(5);
    });
  });

  describe('Skip Rules', () => {
    test('should add skip rule', () => {
      skipper.addSkipRule(1, (data) => data.skipStep2);
      expect(skipper.getSkipRules().has(1)).toBe(true);
    });

    test('should evaluate skip rule correctly', () => {
      skipper.addSkipRule(1, (data) => data.skipStep2);

      expect(skipper.shouldSkipStep(1, { skipStep2: false })).toBe(false);
      expect(skipper.shouldSkipStep(1, { skipStep2: true })).toBe(true);
    });

    test('should handle multiple skip rules', () => {
      skipper.addSkipRule(1, (data) => data.skip2);
      skipper.addSkipRule(3, (data) => data.skip4);

      const data = { skip2: true, skip4: false };

      expect(skipper.shouldSkipStep(1, data)).toBe(true);
      expect(skipper.shouldSkipStep(3, data)).toBe(false);
    });

    test('should remove skip rule', () => {
      skipper.addSkipRule(1, (data) => true);
      skipper.removeSkipRule(1);

      expect(skipper.getSkipRules().has(1)).toBe(false);
      expect(skipper.shouldSkipStep(1, {})).toBe(false);
    });
  });

  describe('Step Skipping Status', () => {
    test('should mark step as skipped', () => {
      skipper.markSkipped(1, 'user request');
      expect(skipper.wasStepSkipped(1)).toBe(true);
      expect(skipper.getSkippedSteps()).toContain(1);
    });

    test('should mark step as not skipped', () => {
      skipper.markSkipped(1);
      skipper.markNotSkipped(1);

      expect(skipper.wasStepSkipped(1)).toBe(false);
    });

    test('should get all skipped steps', () => {
      skipper.markSkipped(1);
      skipper.markSkipped(3);

      const skipped = skipper.getSkippedSteps();
      expect(skipped).toContain(1);
      expect(skipped).toContain(3);
      expect(skipped).toHaveLength(2);
    });
  });

  describe('Navigable Steps', () => {
    test('should get next navigable step', () => {
      skipper.markSkipped(1);
      const next = skipper.getNextNavigableStep(0);

      expect(next).toBe(2);
    });

    test('should get previous navigable step', () => {
      skipper.markSkipped(2);
      const prev = skipper.getPreviousNavigableStep(3);

      expect(prev).toBe(1);
    });

    test('should return null when no next navigable step', () => {
      skipper.markSkipped(3);
      skipper.markSkipped(4);

      const next = skipper.getNextNavigableStep(2);
      expect(next).toBeNull();
    });

    test('should return null when no previous navigable step', () => {
      skipper.markSkipped(0);
      const prev = skipper.getPreviousNavigableStep(1);

      expect(prev).toBeNull();
    });

    test('should count navigable steps correctly', () => {
      skipper.markSkipped(1);
      skipper.markSkipped(3);

      expect(skipper.getNavigableStepCount()).toBe(3);
    });
  });

  describe('Auto Navigation', () => {
    test('should auto-navigate past skipped step', () => {
      skipper.addSkipRule(0, (data) => data.skipFirst);

      const navigated = skipper.autoNavigatePastSkipped({ skipFirst: true });

      expect(navigated).toBe(true);
      expect(skipper.getSkippedSteps()).toContain(0);
    });

    test('should not navigate if current step not skipped', () => {
      skipper.addSkipRule(1, (data) => true);

      const navigated = skipper.autoNavigatePastSkipped({});

      expect(navigated).toBe(false);
    });

    test('should not navigate if no next navigable step', () => {
      skipper.markSkipped(0);
      skipper.markSkipped(1);
      skipper.markSkipped(2);
      skipper.markSkipped(3);
      skipper.markSkipped(4);

      const navigated = skipper.autoNavigatePastSkipped({});

      expect(navigated).toBe(false);
    });
  });

  describe('Skip History', () => {
    test('should record skip events', () => {
      skipper.markSkipped(1, 'test reason');
      skipper.markSkipped(2, 'another reason');

      const history = skipper.getSkipHistory();
      expect(history).toHaveLength(2);
      expect(history[0].stepIndex).toBe(1);
      expect(history[0].reason).toContain('test reason');
    });

    test('should trigger callback on skip', () => {
      const skipped: any[] = [];
      skipper.onSkipped((event) => skipped.push(event));

      skipper.markSkipped(1, 'test');

      expect(skipped).toHaveLength(1);
      expect(skipped[0].stepIndex).toBe(1);
    });

    test('should clear skip history', () => {
      skipper.markSkipped(1);
      skipper.markSkipped(2);

      skipper.clearSkipHistory();

      expect(skipper.getSkipHistory()).toHaveLength(0);
    });

    test('should limit history size', () => {
      const maxSize = 100;

      for (let i = 0; i < 150; i++) {
        skipper.markSkipped(i % 5);
      }

      const history = skipper.getSkipHistory();
      expect(history.length).toBeLessThanOrEqual(maxSize);
    });
  });

  describe('Integration with ConditionalStepManager', () => {
    test('should skip hidden steps', () => {
      const conditional = new ConditionalStepManager(stepper);
      const integrationSkipper = new StepSkipper(stepper, conditional);

      conditional.addVisibilityRule(1, (data) => false);
      conditional.updateVisibility({});

      expect(integrationSkipper.shouldSkipStep(1)).toBe(true);
    });

    test('should not skip visible steps without skip rule', () => {
      const conditional = new ConditionalStepManager(stepper);
      const integrationSkipper = new StepSkipper(stepper, conditional);

      conditional.addVisibilityRule(1, (data) => true);
      conditional.updateVisibility({});

      expect(integrationSkipper.shouldSkipStep(1)).toBe(false);
    });
  });

  describe('Error Handling', () => {
    test('should throw on invalid step index', () => {
      expect(() => skipper.addSkipRule(-1, () => true)).toThrow();
      expect(() => skipper.addSkipRule(5, () => true)).toThrow();
      expect(() => skipper.markSkipped(10)).toThrow();
    });

    test('should handle rule evaluation errors gracefully', () => {
      skipper.addSkipRule(1, () => {
        throw new Error('Rule error');
      });

      expect(() => skipper.shouldSkipStep(1, {})).not.toThrow();
      expect(skipper.shouldSkipStep(1, {})).toBe(false);
    });
  });

  describe('State Management', () => {
    test('should reset all skip state', () => {
      skipper.addSkipRule(1, (data) => true);
      skipper.markSkipped(2);

      skipper.reset();

      expect(skipper.getSkipRules().size).toBe(0);
      expect(skipper.getSkippedSteps()).toHaveLength(0);
      expect(skipper.getSkipHistory()).toHaveLength(0);
    });

    test('should provide skip summary', () => {
      skipper.markSkipped(1);
      skipper.markSkipped(3);
      skipper.addSkipRule(2, (data) => true);

      const summary = skipper.getSkipSummary();

      expect(summary.totalSteps).toBe(5);
      expect(summary.skippedSteps).toBe(2);
      expect(summary.navigableSteps).toBe(3);
      expect(summary.rulesCount).toBe(1);
    });
  });

  describe('Complex Scenarios', () => {
    test('should handle conditional skip rules', () => {
      skipper.addSkipRule(1, (data) => {
        return data.userType === 'basic';
      });

      expect(skipper.shouldSkipStep(1, { userType: 'basic' })).toBe(true);
      expect(skipper.shouldSkipStep(1, { userType: 'premium' })).toBe(false);
    });

    test('should handle skip rules with multiple conditions', () => {
      skipper.addSkipRule(2, (data) => {
        return data.country === 'US' && data.needsState;
      });

      expect(skipper.shouldSkipStep(2, { country: 'US', needsState: true })).toBe(true);
      expect(skipper.shouldSkipStep(2, { country: 'CA', needsState: true })).toBe(false);
      expect(skipper.shouldSkipStep(2, { country: 'US', needsState: false })).toBe(false);
    });

    test('should navigate through multiple skipped steps', () => {
      skipper.markSkipped(1);
      skipper.markSkipped(2);

      let next = skipper.getNextNavigableStep(0);
      expect(next).toBe(3);

      next = skipper.getNextNavigableStep(3);
      expect(next).toBe(4);
    });

    test('should maintain chainable API', () => {
      const result = skipper
        .addSkipRule(1, (data) => true)
        .markSkipped(2)
        .markNotSkipped(2)
        .clearSkipHistory();

      expect(result).toBe(skipper);
    });
  });

  describe('Performance', () => {
    test('should evaluate skip rules quickly', () => {
      for (let i = 0; i < 5; i++) {
        skipper.addSkipRule(i, (data) => Math.random() > 0.5);
      }

      const start = performance.now();

      for (let j = 0; j < 10000; j++) {
        skipper.shouldSkipStep(j % 5, { test: true });
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(100);
    });

    test('should get navigable steps quickly', () => {
      for (let i = 0; i < 5; i++) {
        skipper.markSkipped(i);
      }

      const start = performance.now();

      for (let j = 0; j < 1000; j++) {
        skipper.getNavigableStepCount();
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(50);
    });
  });
});
