import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  docsSidebar: [
    'introduction',
    {
      type: 'category',
      label: 'Getting Started',
      collapsed: false,
      items: [
        'getting-started/prerequisites',
        'getting-started/quick-start',
        'getting-started/first-module',
      ],
    },
    {
      type: 'category',
      label: 'Architecture',
      items: [
        'architecture/overview',
        'architecture/module-system',
        'architecture/service-lifecycle',
        'architecture/clean-architecture',
      ],
    },
    {
      type: 'category',
      label: 'Data Layer',
      items: [
        'data/overview',
        'data/entity-framework',
        'data/mongodb',
        'data/repositories',
        'data/migrations',
      ],
    },
    {
      type: 'category',
      label: 'Messaging',
      items: [
        'messaging/overview',
        'messaging/mediator',
        'messaging/events',
        'messaging/outbox',
        'messaging/inbox',
        'messaging/rabbitmq',
        'messaging/kafka',
      ],
    },
    {
      type: 'category',
      label: 'Identity',
      items: [
        'identity/overview',
        'identity/openiddict',
        'identity/external-providers',
      ],
    },
    {
      type: 'category',
      label: 'Platform Services',
      items: [
        'platform/overview',
        'platform/multi-tenancy',
        'platform/authorization',
        'platform/background-jobs',
        'platform/caching',
        'platform/storage',
        'platform/signalr',
      ],
    },
    {
      type: 'category',
      label: 'Production Hardening',
      items: [
        'hardening/rate-limiting',
        'hardening/health-checks',
        'hardening/cors',
        'hardening/security-headers',
        'hardening/idempotency',
        'hardening/correlation',
        'hardening/feature-flags',
        'hardening/secrets-guard',
        'hardening/personal-data-protection',
      ],
    },
    {
      type: 'category',
      label: 'Observability',
      items: [
        'observability/overview',
        'observability/opentelemetry',
      ],
    },
    {
      type: 'category',
      label: 'Testing',
      items: [
        'testing/overview',
        'testing/integration-tests',
      ],
    },
    {
      type: 'category',
      label: 'Configuration',
      items: [
        'configuration/build-system',
        'configuration/central-package-management',
        'configuration/appsettings',
      ],
    },
    'deployment',
    'samples',
  ],
  cliSidebar: [
    'cli/index',
    {
      type: 'category',
      label: 'Commands',
      items: [
        'cli/app',
        'cli/module',
        'cli/add-module',
        'cli/generate-crud',
        'cli/generate-command',
        'cli/generate-query',
        'cli/migrate',
        'cli/list',
        'cli/info',
        'cli/doctor',
        'cli/outdated',
        'cli/update',
      ],
    },
    'cli/global-flags',
    'cli/templates',
  ],
  apiSidebar: [
    'api/index',
    {
      type: 'category',
      label: 'Core',
      items: [
        'api/core/module',
        'api/core/ddd',
        'api/core/abstractions',
      ],
    },
    {
      type: 'category',
      label: 'Data',
      items: [
        'api/data/repository',
        'api/data/db-context',
      ],
    },
    {
      type: 'category',
      label: 'Mediator',
      items: [
        'api/mediator/commands',
        'api/mediator/queries',
        'api/mediator/behaviors',
      ],
    },
    {
      type: 'category',
      label: 'ASP.NET Core',
      items: [
        'api/aspnetcore/endpoints',
        'api/aspnetcore/middleware',
      ],
    },
  ],
};

export default sidebars;
