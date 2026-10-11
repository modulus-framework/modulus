/**
 * TabStepper - Tab-based stepper with multiple layout options
 * Provides horizontal tabs, vertical tabs, accordion, and carousel layouts
 */

import { Stepper, Step } from './stepper';

export type TabLayout = 'horizontal' | 'vertical' | 'accordion' | 'carousel';

export interface TabStepperOptions {
  layout?: TabLayout;
  showStepNumbers?: boolean;
  showStepDescriptions?: boolean;
  animateTransitions?: boolean;
  tabPosition?: 'top' | 'left' | 'bottom' | 'right';
}

export class TabStepper extends Stepper {
  private currentLayout: TabLayout = 'horizontal';
  private options: TabStepperOptions;
  private tabContainer: HTMLElement | null = null;
  private onLayoutChange: ((layout: TabLayout) => void) | null = null;

  constructor(
    containerElement: HTMLElement,
    steps: Step[],
    options?: TabStepperOptions
  ) {
    super(containerElement, steps, options);
    this.options = {
      showStepNumbers: true,
      showStepDescriptions: true,
      animateTransitions: true,
      tabPosition: 'top',
      ...options
    };
    this.currentLayout = options?.layout || 'horizontal';
    this.initializeTabLayout();
  }

  /**
   * Initialize tab-based layout
   */
  private initializeTabLayout(): void {
    this.createTabContainer();
    this.renderTabs();
  }

  /**
   * Create tab container element
   */
  private createTabContainer(): void {
    const container = this.getContainer();
    this.tabContainer = document.createElement('div');
    this.tabContainer.className = `step-tabs step-tabs-${this.currentLayout}`;

    if (this.options.tabPosition && this.currentLayout === 'vertical') {
      this.tabContainer.classList.add(`tabs-${this.options.tabPosition}`);
    }

    // Insert at beginning for top position, or appropriate position
    if (this.options.tabPosition === 'left' || this.options.tabPosition === 'bottom') {
      container.appendChild(this.tabContainer);
    } else {
      container.insertBefore(this.tabContainer, container.firstChild);
    }
  }

  /**
   * Render tabs based on current layout
   */
  renderTabs(): HTMLElement | null {
    if (!this.tabContainer) {
      this.createTabContainer();
    }

    // Clear existing tabs
    if (this.tabContainer) {
      this.tabContainer.innerHTML = '';
    }

    switch (this.currentLayout) {
      case 'horizontal':
        return this.renderHorizontalTabs();
      case 'vertical':
        return this.renderVerticalTabs();
      case 'accordion':
        return this.renderAccordionTabs();
      case 'carousel':
        return this.renderCarouselTabs();
      default:
        return this.renderHorizontalTabs();
    }
  }

  /**
   * Render horizontal tabs
   */
  private renderHorizontalTabs(): HTMLElement | null {
    if (!this.tabContainer) return null;

    const tabList = document.createElement('div');
    tabList.className = 'tab-list horizontal';
    tabList.setAttribute('role', 'tablist');

    const steps = this.getSteps();
    const currentIndex = this.getCurrentStepIndex();

    steps.forEach((step, index) => {
      const tab = this.createTabElement(step, index, currentIndex === index);
      tabList.appendChild(tab);
    });

    this.tabContainer.appendChild(tabList);
    return this.tabContainer;
  }

  /**
   * Render vertical tabs
   */
  private renderVerticalTabs(): HTMLElement | null {
    if (!this.tabContainer) return null;

    const tabList = document.createElement('div');
    tabList.className = 'tab-list vertical';
    tabList.setAttribute('role', 'tablist');

    const steps = this.getSteps();
    const currentIndex = this.getCurrentStepIndex();

    steps.forEach((step, index) => {
      const tab = this.createTabElement(step, index, currentIndex === index);
      tab.classList.add('vertical-tab');
      tabList.appendChild(tab);
    });

    this.tabContainer.appendChild(tabList);
    return this.tabContainer;
  }

  /**
   * Render accordion tabs
   */
  private renderAccordionTabs(): HTMLElement | null {
    if (!this.tabContainer) return null;

    const accordion = document.createElement('div');
    accordion.className = 'accordion-list';
    accordion.setAttribute('role', 'region');

    const steps = this.getSteps();
    const currentIndex = this.getCurrentStepIndex();

    steps.forEach((step, index) => {
      const item = document.createElement('div');
      item.className = 'accordion-item';

      const header = document.createElement('button');
      header.className = 'accordion-header';
      header.setAttribute('aria-expanded', (index === currentIndex).toString());
      header.setAttribute('aria-controls', `step-${step.id}`);

      if (this.options.showStepNumbers) {
        const number = document.createElement('span');
        number.className = 'step-number';
        number.textContent = `${index + 1}`;
        header.appendChild(number);
      }

      const title = document.createElement('span');
      title.className = 'step-title';
      title.textContent = step.title;
      header.appendChild(title);

      header.addEventListener('click', () => this.goToStep(index));

      const content = document.createElement('div');
      content.id = `step-${step.id}`;
      content.className = 'accordion-content';
      content.style.display = index === currentIndex ? 'block' : 'none';

      if (this.options.showStepDescriptions && step.description) {
        const description = document.createElement('p');
        description.className = 'step-description';
        description.textContent = step.description;
        content.appendChild(description);
      }

      item.appendChild(header);
      item.appendChild(content);
      accordion.appendChild(item);
    });

    this.tabContainer.appendChild(accordion);
    return this.tabContainer;
  }

  /**
   * Render carousel tabs
   */
  private renderCarouselTabs(): HTMLElement | null {
    if (!this.tabContainer) return null;

    const carousel = document.createElement('div');
    carousel.className = 'carousel-container';

    const track = document.createElement('div');
    track.className = 'carousel-track';

    const steps = this.getSteps();
    const currentIndex = this.getCurrentStepIndex();

    steps.forEach((step, index) => {
      const slide = document.createElement('div');
      slide.className = 'carousel-slide';
      slide.setAttribute('data-index', index.toString());

      if (index === currentIndex) {
        slide.classList.add('active');
      }

      const label = document.createElement('button');
      label.className = 'carousel-label';
      label.textContent = step.title;
      label.addEventListener('click', () => this.goToStep(index));

      slide.appendChild(label);
      track.appendChild(slide);
    });

    carousel.appendChild(track);
    this.tabContainer.appendChild(carousel);
    return this.tabContainer;
  }

  /**
   * Create individual tab element
   */
  private createTabElement(step: Step, index: number, isActive: boolean): HTMLElement {
    const tab = document.createElement('button');
    tab.className = 'step-tab';
    tab.setAttribute('role', 'tab');
    tab.setAttribute('aria-selected', isActive.toString());
    tab.setAttribute('aria-controls', `step-${step.id}`);
    tab.setAttribute('id', `tab-${step.id}`);

    if (isActive) {
      tab.classList.add('active');
    }

    if (this.options.showStepNumbers) {
      const number = document.createElement('span');
      number.className = 'step-number';
      number.textContent = `${index + 1}`;
      tab.appendChild(number);
    }

    const title = document.createElement('span');
    title.className = 'step-title';
    title.textContent = step.title;
    tab.appendChild(title);

    if (this.options.showStepDescriptions && step.description) {
      const description = document.createElement('span');
      description.className = 'step-description';
      description.textContent = step.description;
      tab.appendChild(description);
    }

    tab.addEventListener('click', () => {
      this.goToStep(index);
      this.updateTabs();
    });

    return tab;
  }

  /**
   * Update tab selection
   */
  private updateTabs(): void {
    if (!this.tabContainer) return;

    const tabs = this.tabContainer.querySelectorAll('.step-tab');
    const currentIndex = this.getCurrentStepIndex();

    tabs.forEach((tab, index) => {
      if (index === currentIndex) {
        tab.classList.add('active');
        tab.setAttribute('aria-selected', 'true');
      } else {
        tab.classList.remove('active');
        tab.setAttribute('aria-selected', 'false');
      }
    });

    // Update accordion
    const accordionItems = this.tabContainer.querySelectorAll('.accordion-item');
    accordionItems.forEach((item, index) => {
      const header = item.querySelector('.accordion-header') as HTMLElement;
      const content = item.querySelector('.accordion-content') as HTMLElement;

      if (index === currentIndex) {
        header?.setAttribute('aria-expanded', 'true');
        if (content) content.style.display = 'block';
      } else {
        header?.setAttribute('aria-expanded', 'false');
        if (content) content.style.display = 'none';
      }
    });

    // Update carousel
    const slides = this.tabContainer.querySelectorAll('.carousel-slide');
    slides.forEach((slide, index) => {
      if (index === currentIndex) {
        slide.classList.add('active');
      } else {
        slide.classList.remove('active');
      }
    });
  }

  /**
   * Set layout
   */
  setLayout(layout: TabLayout): TabStepper {
    if (this.currentLayout === layout) return this;

    this.currentLayout = layout;
    this.renderTabs();
    this.updateTabs();

    if (this.onLayoutChange) {
      this.onLayoutChange(layout);
    }

    return this;
  }

  /**
   * Get current layout
   */
  getLayout(): TabLayout {
    return this.currentLayout;
  }

  /**
   * Switch layout with animation
   */
  switchLayout(layout: TabLayout): TabStepper {
    if (this.options.animateTransitions && this.tabContainer) {
      this.tabContainer.classList.add('transitioning');
      setTimeout(() => {
        this.setLayout(layout);
        this.tabContainer?.classList.remove('transitioning');
      }, 300);
    } else {
      this.setLayout(layout);
    }

    return this;
  }

  /**
   * Get available layouts
   */
  getAvailableLayouts(): TabLayout[] {
    return ['horizontal', 'vertical', 'accordion', 'carousel'];
  }

  /**
   * Override goToStep to update tabs
   */
  goToStep(index: number): boolean {
    const result = super.goToStep(index);
    if (result) {
      this.updateTabs();
    }
    return result;
  }

  /**
   * Override nextStep to update tabs
   */
  nextStep(): boolean {
    const result = super.nextStep();
    if (result) {
      this.updateTabs();
    }
    return result;
  }

  /**
   * Override previousStep to update tabs
   */
  previousStep(): void {
    super.previousStep();
    this.updateTabs();
  }

  /**
   * Set layout change callback
   */
  onLayoutChanged(callback: (layout: TabLayout) => void): TabStepper {
    this.onLayoutChange = callback;
    return this;
  }

  /**
   * Get tab container element
   */
  getTabContainer(): HTMLElement | null {
    return this.tabContainer;
  }

  /**
   * Export layout configuration
   */
  exportConfig(): {
    layout: TabLayout;
    options: TabStepperOptions;
  } {
    return {
      layout: this.currentLayout,
      options: { ...this.options }
    };
  }
}
