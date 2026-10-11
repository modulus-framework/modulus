/**
 * Phase7IntegrationTests - Integration tests for all Phase 7 components
 * Tests complete workflows with visibility, dynamic steps, layouts, and branching
 */

import { Stepper } from '../wwwroot/js/stepper';
import { ConditionalStepManager } from '../wwwroot/js/conditional-step-manager';
import { StepSkipper } from '../wwwroot/js/step-skipper';
import { DynamicStepHandler } from '../wwwroot/js/dynamic-step-handler';
import { TabStepper } from '../wwwroot/js/tab-stepper';
import { WorkflowEngine } from '../wwwroot/js/workflow-engine';
import { FormStateManager } from '../wwwroot/js/form-state-manager';
import { StepValidator } from '../wwwroot/js/step-validator';

describe('Phase 7 Integration - Complete Workflows', () => {
  let containerElement: HTMLElement;
  let stepper: Stepper;
  let conditional: ConditionalStepManager;
  let skipper: StepSkipper;
  let stateManager: FormStateManager;
  let validator: StepValidator;

  beforeEach(() => {
    containerElement = document.createElement('div');
    document.body.appendChild(containerElement);

    const steps = [
      { id: 's1', title: 'Personal Info' },
      { id: 's2', title: 'Contact Details' },
      { id: 's3', title: 'Address' },
      { id: 's4', title: 'Preferences' },
      { id: 's5', title: 'Review & Submit' }
    ];

    stepper = new Stepper(containerElement, steps);
    conditional = new ConditionalStepManager(stepper);
    skipper = new StepSkipper(stepper, conditional);
    stateManager = new FormStateManager('memory');
    validator = new StepValidator(stepper);
  });

  afterEach(() => {
    document.body.removeChild(containerElement);
  });

  describe('Conditional Visibility Integration', () => {
    test('should hide steps based on data and auto-navigate', () => {
      conditional.addVisibilityRule(2, (data) => data.showAddress);

      conditional.updateVisibility({ showAddress: false });
      expect(conditional.isStepVisible(2)).toBe(false);

      // Should navigate past hidden step
      skipper.autoNavigatePastSkipped({ showAddress: false });
      expect(skipper.getNavigableStepCount({ showAddress: false })).toBe(4);
    });

    test('should show address step when needed', () => {
      conditional.addVisibilityRule(2, (data) => data.needsAddress);

      conditional.updateVisibility({ needsAddress: false });
      expect(conditional.getVisibleSteps()).not.toContain(2);

      conditional.updateVisibility({ needsAddress: true });
      expect(conditional.getVisibleSteps()).toContain(2);
    });

    test('should handle complex visibility conditions', () => {
      // Show address only for US users
      conditional.addVisibilityRule(2, (data) => data.country === 'US');

      // Show state field only if address shown
      conditional.addVisibilityRule(3, (data) => {
        return data.country === 'US' && data.hasAddress;
      }, [2]);

      conditional.updateVisibility({ country: 'US', hasAddress: true });

      expect(conditional.isStepVisible(2)).toBe(true);
      expect(conditional.isStepVisible(3)).toBe(true);

      conditional.updateVisibility({ country: 'CA', hasAddress: true });

      expect(conditional.isStepVisible(2)).toBe(false);
      expect(conditional.isStepVisible(3)).toBe(false);
    });
  });

  describe('Dynamic Step Management Integration', () => {
    test('should add optional steps dynamically', () => {
      const handler = new DynamicStepHandler(stepper);

      expect(stepper.getStepCount()).toBe(5);

      handler.addStep(2, { id: 'optional', title: 'Optional Info' });

      expect(stepper.getStepCount()).toBe(6);
      expect(handler.isDynamicStep(2)).toBe(true);
    });

    test('should remove steps and preserve state', () => {
      const handler = new DynamicStepHandler(stepper, stateManager);

      stateManager.setState('step_2_data', { field: 'value' });

      handler.removeStep(2);

      const preserved = stateManager.getState('removed_step_2_data');
      expect(preserved).toEqual({ field: 'value' });
    });

    test('should reorder steps with validation rules', () => {
      const handler = new DynamicStepHandler(stepper);
      validator.addStepRule(0, (data) => true, 'Step 0');
      validator.addStepRule(1, (data) => true, 'Step 1');

      handler.reorderSteps([1, 0, 2, 3, 4]);

      expect(stepper.getStepCount()).toBe(5);
    });
  });

  describe('Tab Layout Integration', () => {
    test('should render tabs and navigate', () => {
      const tabStepper = new TabStepper(containerElement, [
        { id: 's1', title: 'Step 1' },
        { id: 's2', title: 'Step 2' }
      ]);

      expect(tabStepper.getLayout()).toBe('horizontal');
      const tabs = tabStepper.getTabContainer()?.querySelectorAll('.step-tab');
      expect(tabs?.length).toBe(2);
    });

    test('should switch layouts smoothly', () => {
      const tabStepper = new TabStepper(containerElement, [
        { id: 's1', title: 'Step 1' },
        { id: 's2', title: 'Step 2' }
      ]);

      tabStepper.setLayout('vertical');
      expect(tabStepper.getLayout()).toBe('vertical');

      tabStepper.setLayout('accordion');
      expect(tabStepper.getLayout()).toBe('accordion');
    });

    test('should update tabs when using dynamic steps', () => {
      const tabStepper = new TabStepper(containerElement, [
        { id: 's1', title: 'Step 1' },
        { id: 's2', title: 'Step 2' }
      ]);

      const handler = new DynamicStepHandler(tabStepper);
      handler.addStep(1, { id: 'new', title: 'New Step' });

      expect(tabStepper.getStepCount()).toBe(3);
      // Note: In real implementation, tabs would need to be re-rendered
    });
  });

  describe('Workflow Engine Integration', () => {
    test('should evaluate workflow branches', () => {
      const engine = new WorkflowEngine(stepper, conditional, skipper);

      // Define business vs individual workflow
      engine.defineWorkflow({
        name: 'registration',
        branches: [
          {
            name: 'business',
            condition: (data) => data.type === 'business',
            path: [0, 1, 3, 4] // Skip address for business
          },
          {
            name: 'individual',
            condition: (data) => data.type === 'individual',
            path: [0, 1, 2, 3, 4] // Include address for individual
          }
        ]
      });

      // Activate for business
      engine.activateWorkflow('registration', { type: 'business' });

      expect(engine.getCurrentBranch()).toBe('business');
      expect(engine.getActivePath()).toEqual([0, 1, 3, 4]);
    });

    test('should navigate within workflow path', () => {
      const engine = new WorkflowEngine(stepper);

      engine.defineWorkflow({
        name: 'test',
        branches: [
          {
            name: 'path1',
            condition: () => true,
            path: [0, 2, 4]
          }
        ]
      });

      engine.activateWorkflow('test', {});

      const next = engine.getNextPathStep(0);
      expect(next).toBe(2);

      const prev = engine.getPreviousPathStep(4);
      expect(prev).toBe(2);
    });

    test('should track workflow progress', () => {
      const engine = new WorkflowEngine(stepper);

      engine.defineWorkflow({
        name: 'test',
        branches: [
          {
            name: 'path',
            condition: () => true,
            path: [0, 1, 2]
          }
        ]
      });

      engine.activateWorkflow('test', {});

      expect(engine.getProgress()).toBe(0);

      engine.completeStep(0);
      expect(engine.getProgress()).toBe(33);

      engine.completeStep(1);
      engine.completeStep(2);
      expect(engine.isPathComplete()).toBe(true);
    });
  });

  describe('Complete End-to-End Workflow', () => {
    test('should handle complete user journey', async () => {
      const handler = new DynamicStepHandler(stepper, stateManager);
      const engine = new WorkflowEngine(stepper, conditional, skipper);

      // Define workflow
      engine.defineWorkflow({
        name: 'registration',
        branches: [
          {
            name: 'us',
            condition: (data) => data.country === 'US',
            path: [0, 1, 2, 3, 4]
          },
          {
            name: 'intl',
            condition: (data) => data.country !== 'US',
            path: [0, 1, 3, 4]
          }
        ]
      });

      // Step 1: Personal info
      stateManager.setMultipleState({
        country: 'US'
      });

      // Activate workflow
      engine.activateWorkflow('registration', { country: 'US' });
      expect(engine.getCurrentBranch()).toBe('us');

      // Complete step 1
      engine.completeStep(0);
      expect(validator.validateForNextStep()).toBe(true);

      // Navigate through workflow
      let currentIndex = 0;
      while (currentIndex < stepper.getStepCount()) {
        const nextStep = engine.getNextPathStep(currentIndex);
        if (nextStep === null) break;

        stepper.goToStep(nextStep);
        engine.completeStep(nextStep);
        currentIndex = nextStep;
      }

      expect(engine.getProgress()).toBe(100);
      expect(engine.isPathComplete()).toBe(true);
    });

    test('should switch workflows based on data changes', () => {
      const engine = new WorkflowEngine(stepper);

      engine.defineWorkflow({
        name: 'type-selection',
        branches: [
          {
            name: 'simple',
            condition: (data) => data.formType === 'simple',
            path: [0, 4]
          },
          {
            name: 'detailed',
            condition: (data) => data.formType === 'detailed',
            path: [0, 1, 2, 3, 4]
          }
        ]
      });

      // Start with simple workflow
      engine.activateWorkflow('type-selection', { formType: 'simple' });
      expect(engine.getActivePath()).toEqual([0, 4]);

      // Switch to detailed
      engine.activateWorkflow('type-selection', { formType: 'detailed' });
      expect(engine.getActivePath()).toEqual([0, 1, 2, 3, 4]);
    });
  });

  describe('Error Handling', () => {
    test('should handle invalid visibility conditions gracefully', () => {
      conditional.addVisibilityRule(0, () => {
        throw new Error('Bad rule');
      });

      expect(() => conditional.updateVisibility({})).not.toThrow();
      expect(conditional.isStepVisible(0)).toBe(true); // Falls back
    });

    test('should handle invalid branch conditions gracefully', () => {
      const engine = new WorkflowEngine(stepper);

      engine.defineWorkflow({
        name: 'test',
        branches: [
          {
            name: 'bad',
            condition: () => {
              throw new Error('Error');
            },
            path: [0, 1]
          }
        ]
      });

      expect(() => engine.activateWorkflow('test', {})).not.toThrow();
    });
  });

  describe('Performance', () => {
    test('should handle complex workflows efficiently', () => {
      const steps = Array.from({ length: 50 }, (_, i) => ({
        id: `s${i}`,
        title: `Step ${i + 1}`
      }));

      const tempStepper = new Stepper(containerElement, steps);
      const engine = new WorkflowEngine(tempStepper);

      const start = performance.now();

      for (let i = 0; i < 10; i++) {
        engine.defineWorkflow({
          name: `workflow${i}`,
          branches: [
            {
              name: 'branch',
              condition: (data) => Math.random() > 0.5,
              path: Array.from({ length: 25 }, (_, j) => j)
            }
          ]
        });
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(100);
    });
  });
});
