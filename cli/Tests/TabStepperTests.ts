/**
 * TabStepperTests - Test suite for tab-based stepper layouts
 */

import { TabStepper } from '../wwwroot/js/tab-stepper';

describe('TabStepper', () => {
  let containerElement: HTMLElement;
  let stepper: TabStepper;

  beforeEach(() => {
    containerElement = document.createElement('div');
    document.body.appendChild(containerElement);

    const steps = [
      { id: 's1', title: 'Step 1', description: 'First step' },
      { id: 's2', title: 'Step 2', description: 'Second step' },
      { id: 's3', title: 'Step 3', description: 'Third step' },
      { id: 's4', title: 'Step 4', description: 'Fourth step' }
    ];

    stepper = new TabStepper(containerElement, steps, { layout: 'horizontal' });
  });

  afterEach(() => {
    document.body.removeChild(containerElement);
  });

  describe('Initialization', () => {
    test('should initialize with horizontal layout', () => {
      expect(stepper.getLayout()).toBe('horizontal');
    });

    test('should create tab container', () => {
      const tabContainer = stepper.getTabContainer();
      expect(tabContainer).toBeDefined();
      expect(tabContainer).toBeInstanceOf(HTMLElement);
    });

    test('should render tabs on initialization', () => {
      const tabContainer = stepper.getTabContainer();
      const tabs = tabContainer?.querySelectorAll('.step-tab');
      expect(tabs?.length).toBe(4);
    });

    test('should show step numbers by default', () => {
      const tabContainer = stepper.getTabContainer();
      const numbers = tabContainer?.querySelectorAll('.step-number');
      expect(numbers?.length).toBeGreaterThan(0);
    });
  });

  describe('Horizontal Layout', () => {
    test('should render horizontal tabs', () => {
      const tabList = stepper.getTabContainer()?.querySelector('.tab-list.horizontal');
      expect(tabList).toBeDefined();
    });

    test('should have tabs with correct role', () => {
      const tabs = stepper.getTabContainer()?.querySelectorAll('[role="tab"]');
      expect(tabs?.length).toBe(4);
    });

    test('should mark active tab', () => {
      const tabs = stepper.getTabContainer()?.querySelectorAll('.step-tab');
      expect(tabs?.[0].classList.contains('active')).toBe(true);
      expect(tabs?.[1].classList.contains('active')).toBe(false);
    });

    test('should navigate on tab click', () => {
      const tabs = stepper.getTabContainer()?.querySelectorAll('.step-tab');
      const secondTab = tabs?.[1] as HTMLElement;

      secondTab?.click();

      expect(stepper.getCurrentStepIndex()).toBe(1);
    });
  });

  describe('Vertical Layout', () => {
    beforeEach(() => {
      stepper.setLayout('vertical');
    });

    test('should render vertical tabs', () => {
      const tabList = stepper.getTabContainer()?.querySelector('.tab-list.vertical');
      expect(tabList).toBeDefined();
    });

    test('should set vertical class', () => {
      const container = stepper.getTabContainer();
      expect(container?.classList.contains('step-tabs-vertical')).toBe(true);
    });

    test('should navigate on vertical tab click', () => {
      const tabs = stepper.getTabContainer()?.querySelectorAll('.step-tab');
      const thirdTab = tabs?.[2] as HTMLElement;

      thirdTab?.click();

      expect(stepper.getCurrentStepIndex()).toBe(2);
    });
  });

  describe('Accordion Layout', () => {
    beforeEach(() => {
      stepper.setLayout('accordion');
    });

    test('should render accordion items', () => {
      const items = stepper.getTabContainer()?.querySelectorAll('.accordion-item');
      expect(items?.length).toBe(4);
    });

    test('should have accordion headers', () => {
      const headers = stepper.getTabContainer()?.querySelectorAll('.accordion-header');
      expect(headers?.length).toBe(4);
    });

    test('should show content for active item', () => {
      const contents = stepper.getTabContainer()?.querySelectorAll('.accordion-content');
      expect((contents?.[0] as HTMLElement).style.display).toBe('block');
      expect((contents?.[1] as HTMLElement).style.display).toBe('none');
    });

    test('should expand accordion on header click', () => {
      const headers = stepper.getTabContainer()?.querySelectorAll('.accordion-header');
      const secondHeader = headers?.[1] as HTMLElement;

      secondHeader?.click();

      expect(stepper.getCurrentStepIndex()).toBe(1);
    });

    test('should set aria-expanded correctly', () => {
      const headers = stepper.getTabContainer()?.querySelectorAll('.accordion-header');
      expect(headers?.[0].getAttribute('aria-expanded')).toBe('true');
      expect(headers?.[1].getAttribute('aria-expanded')).toBe('false');
    });
  });

  describe('Carousel Layout', () => {
    beforeEach(() => {
      stepper.setLayout('carousel');
    });

    test('should render carousel container', () => {
      const carousel = stepper.getTabContainer()?.querySelector('.carousel-container');
      expect(carousel).toBeDefined();
    });

    test('should have carousel slides', () => {
      const slides = stepper.getTabContainer()?.querySelectorAll('.carousel-slide');
      expect(slides?.length).toBe(4);
    });

    test('should mark active slide', () => {
      const slides = stepper.getTabContainer()?.querySelectorAll('.carousel-slide');
      expect(slides?.[0].classList.contains('active')).toBe(true);
      expect(slides?.[1].classList.contains('active')).toBe(false);
    });

    test('should navigate on carousel label click', () => {
      const labels = stepper.getTabContainer()?.querySelectorAll('.carousel-label');
      const thirdLabel = labels?.[2] as HTMLElement;

      thirdLabel?.click();

      expect(stepper.getCurrentStepIndex()).toBe(2);
    });
  });

  describe('Layout Switching', () => {
    test('should switch from horizontal to vertical', () => {
      stepper.setLayout('vertical');
      expect(stepper.getLayout()).toBe('vertical');
      expect(stepper.getTabContainer()?.classList.contains('step-tabs-vertical')).toBe(true);
    });

    test('should switch from vertical to accordion', () => {
      stepper.setLayout('vertical');
      stepper.setLayout('accordion');

      expect(stepper.getLayout()).toBe('accordion');
      expect(stepper.getTabContainer()?.querySelector('.accordion-item')).toBeDefined();
    });

    test('should switch from accordion to carousel', () => {
      stepper.setLayout('accordion');
      stepper.setLayout('carousel');

      expect(stepper.getLayout()).toBe('carousel');
      expect(stepper.getTabContainer()?.querySelector('.carousel-container')).toBeDefined();
    });

    test('should trigger layout change callback', () => {
      const callbacks: any[] = [];
      stepper.onLayoutChanged((layout) => callbacks.push(layout));

      stepper.setLayout('vertical');
      stepper.setLayout('accordion');

      expect(callbacks).toHaveLength(2);
      expect(callbacks[0]).toBe('vertical');
      expect(callbacks[1]).toBe('accordion');
    });

    test('should animate layout switch', (done) => {
      const options = { animateTransitions: true };
      const steps = [
        { id: 's1', title: 'Step 1' },
        { id: 's2', title: 'Step 2' }
      ];

      const animStepper = new TabStepper(containerElement, steps, options);
      const container = animStepper.getTabContainer();

      animStepper.switchLayout('vertical');

      // Check transitioning class
      expect(container?.classList.contains('transitioning')).toBe(true);

      setTimeout(() => {
        // After animation
        expect(animStepper.getLayout()).toBe('vertical');
        done();
      }, 350);
    });
  });

  describe('Tab Navigation', () => {
    test('should update tabs on goToStep', () => {
      stepper.goToStep(2);

      const tabs = stepper.getTabContainer()?.querySelectorAll('.step-tab');
      expect(tabs?.[2].classList.contains('active')).toBe(true);
      expect(tabs?.[0].classList.contains('active')).toBe(false);
    });

    test('should update tabs on nextStep', () => {
      stepper.nextStep();

      const tabs = stepper.getTabContainer()?.querySelectorAll('.step-tab');
      expect(tabs?.[1].classList.contains('active')).toBe(true);
    });

    test('should update tabs on previousStep', () => {
      stepper.goToStep(2);
      stepper.previousStep();

      const tabs = stepper.getTabContainer()?.querySelectorAll('.step-tab');
      expect(tabs?.[1].classList.contains('active')).toBe(true);
    });
  });

  describe('Options', () => {
    test('should hide step numbers when disabled', () => {
      const steps = [{ id: 's1', title: 'Step 1' }];
      const noNumberStepper = new TabStepper(containerElement, steps, {
        showStepNumbers: false
      });

      const numbers = noNumberStepper.getTabContainer()?.querySelectorAll('.step-number');
      expect(numbers?.length).toBe(0);
    });

    test('should hide descriptions when disabled', () => {
      const steps = [{ id: 's1', title: 'Step 1', description: 'Desc' }];
      const noDescStepper = new TabStepper(containerElement, steps, {
        showStepDescriptions: false
      });

      const descriptions = noDescStepper.getTabContainer()?.querySelectorAll('.step-description');
      expect(descriptions?.length).toBe(0);
    });

    test('should support tab position option', () => {
      const steps = [{ id: 's1', title: 'Step 1' }];
      const leftTabStepper = new TabStepper(containerElement, steps, {
        layout: 'vertical',
        tabPosition: 'left'
      });

      const container = leftTabStepper.getTabContainer();
      expect(container?.classList.contains('tabs-left')).toBe(true);
    });
  });

  describe('Available Layouts', () => {
    test('should return all available layouts', () => {
      const layouts = stepper.getAvailableLayouts();
      expect(layouts).toContain('horizontal');
      expect(layouts).toContain('vertical');
      expect(layouts).toContain('accordion');
      expect(layouts).toContain('carousel');
      expect(layouts.length).toBe(4);
    });
  });

  describe('Configuration Export', () => {
    test('should export current configuration', () => {
      stepper.setLayout('vertical');
      const config = stepper.exportConfig();

      expect(config.layout).toBe('vertical');
      expect(config.options.layout).toBe('vertical');
    });

    test('should export all options', () => {
      const config = stepper.exportConfig();

      expect(config.options).toHaveProperty('showStepNumbers');
      expect(config.options).toHaveProperty('showStepDescriptions');
      expect(config.options).toHaveProperty('animateTransitions');
    });
  });

  describe('Accessibility', () => {
    test('should have proper role attributes', () => {
      const tabList = stepper.getTabContainer()?.querySelector('[role="tablist"]');
      expect(tabList).toBeDefined();
    });

    test('should set aria-selected on tabs', () => {
      const tabs = stepper.getTabContainer()?.querySelectorAll('[role="tab"]');
      expect(tabs?.[0].getAttribute('aria-selected')).toBe('true');
      expect(tabs?.[1].getAttribute('aria-selected')).toBe('false');
    });

    test('should set aria-controls on tabs', () => {
      const tabs = stepper.getTabContainer()?.querySelectorAll('[role="tab"]');
      expect(tabs?.[0].getAttribute('aria-controls')).toBe('step-s1');
    });

    test('should have accordion region role', () => {
      stepper.setLayout('accordion');
      const region = stepper.getTabContainer()?.querySelector('[role="region"]');
      expect(region).toBeDefined();
    });
  });

  describe('Performance', () => {
    test('should render 100 tabs quickly', () => {
      const steps = Array.from({ length: 100 }, (_, i) => ({
        id: `s${i}`,
        title: `Step ${i + 1}`
      }));

      const start = performance.now();
      const perfStepper = new TabStepper(containerElement, steps);
      const elapsed = performance.now() - start;

      expect(elapsed).toBeLessThan(500);
    });

    test('should switch layouts quickly', () => {
      const start = performance.now();

      stepper.setLayout('vertical');
      stepper.setLayout('accordion');
      stepper.setLayout('carousel');
      stepper.setLayout('horizontal');

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(200);
    });
  });
});
