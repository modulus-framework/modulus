import React from 'react';
import ComponentCreator from '@docusaurus/ComponentCreator';

export default [
  {
    path: '/__docusaurus/debug',
    component: ComponentCreator('/__docusaurus/debug', '5ff'),
    exact: true
  },
  {
    path: '/__docusaurus/debug/config',
    component: ComponentCreator('/__docusaurus/debug/config', '5ba'),
    exact: true
  },
  {
    path: '/__docusaurus/debug/content',
    component: ComponentCreator('/__docusaurus/debug/content', 'a2b'),
    exact: true
  },
  {
    path: '/__docusaurus/debug/globalData',
    component: ComponentCreator('/__docusaurus/debug/globalData', 'c3c'),
    exact: true
  },
  {
    path: '/__docusaurus/debug/metadata',
    component: ComponentCreator('/__docusaurus/debug/metadata', '156'),
    exact: true
  },
  {
    path: '/__docusaurus/debug/registry',
    component: ComponentCreator('/__docusaurus/debug/registry', '88c'),
    exact: true
  },
  {
    path: '/__docusaurus/debug/routes',
    component: ComponentCreator('/__docusaurus/debug/routes', '000'),
    exact: true
  },
  {
    path: '/markdown-page',
    component: ComponentCreator('/markdown-page', '53a'),
    exact: true
  },
  {
    path: '/docs',
    component: ComponentCreator('/docs', 'da6'),
    routes: [
      {
        path: '/docs',
        component: ComponentCreator('/docs', 'fa6'),
        routes: [
          {
            path: '/docs',
            component: ComponentCreator('/docs', 'cd4'),
            routes: [
              {
                path: '/docs/api/',
                component: ComponentCreator('/docs/api/', 'b2d'),
                exact: true,
                sidebar: "apiSidebar"
              },
              {
                path: '/docs/api/aspnetcore/endpoints',
                component: ComponentCreator('/docs/api/aspnetcore/endpoints', '684'),
                exact: true,
                sidebar: "apiSidebar"
              },
              {
                path: '/docs/api/aspnetcore/middleware',
                component: ComponentCreator('/docs/api/aspnetcore/middleware', '9b6'),
                exact: true,
                sidebar: "apiSidebar"
              },
              {
                path: '/docs/api/core/abstractions',
                component: ComponentCreator('/docs/api/core/abstractions', 'bcf'),
                exact: true,
                sidebar: "apiSidebar"
              },
              {
                path: '/docs/api/core/ddd',
                component: ComponentCreator('/docs/api/core/ddd', '1b2'),
                exact: true,
                sidebar: "apiSidebar"
              },
              {
                path: '/docs/api/core/module',
                component: ComponentCreator('/docs/api/core/module', '841'),
                exact: true,
                sidebar: "apiSidebar"
              },
              {
                path: '/docs/api/data/db-context',
                component: ComponentCreator('/docs/api/data/db-context', 'edb'),
                exact: true,
                sidebar: "apiSidebar"
              },
              {
                path: '/docs/api/data/repository',
                component: ComponentCreator('/docs/api/data/repository', 'dd4'),
                exact: true,
                sidebar: "apiSidebar"
              },
              {
                path: '/docs/api/mediator/behaviors',
                component: ComponentCreator('/docs/api/mediator/behaviors', '5c9'),
                exact: true,
                sidebar: "apiSidebar"
              },
              {
                path: '/docs/api/mediator/commands',
                component: ComponentCreator('/docs/api/mediator/commands', '575'),
                exact: true,
                sidebar: "apiSidebar"
              },
              {
                path: '/docs/api/mediator/queries',
                component: ComponentCreator('/docs/api/mediator/queries', '2cf'),
                exact: true,
                sidebar: "apiSidebar"
              },
              {
                path: '/docs/architecture/clean-architecture',
                component: ComponentCreator('/docs/architecture/clean-architecture', '44b'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/architecture/module-system',
                component: ComponentCreator('/docs/architecture/module-system', '693'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/architecture/overview',
                component: ComponentCreator('/docs/architecture/overview', '8f4'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/architecture/service-lifecycle',
                component: ComponentCreator('/docs/architecture/service-lifecycle', '05a'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/cli/',
                component: ComponentCreator('/docs/cli/', 'f3b'),
                exact: true,
                sidebar: "cliSidebar"
              },
              {
                path: '/docs/cli/add-module',
                component: ComponentCreator('/docs/cli/add-module', '3fd'),
                exact: true,
                sidebar: "cliSidebar"
              },
              {
                path: '/docs/cli/app',
                component: ComponentCreator('/docs/cli/app', 'a31'),
                exact: true,
                sidebar: "cliSidebar"
              },
              {
                path: '/docs/cli/doctor',
                component: ComponentCreator('/docs/cli/doctor', 'a81'),
                exact: true,
                sidebar: "cliSidebar"
              },
              {
                path: '/docs/cli/generate-command',
                component: ComponentCreator('/docs/cli/generate-command', 'e36'),
                exact: true,
                sidebar: "cliSidebar"
              },
              {
                path: '/docs/cli/generate-crud',
                component: ComponentCreator('/docs/cli/generate-crud', '5d7'),
                exact: true,
                sidebar: "cliSidebar"
              },
              {
                path: '/docs/cli/generate-query',
                component: ComponentCreator('/docs/cli/generate-query', '1bc'),
                exact: true,
                sidebar: "cliSidebar"
              },
              {
                path: '/docs/cli/global-flags',
                component: ComponentCreator('/docs/cli/global-flags', '345'),
                exact: true,
                sidebar: "cliSidebar"
              },
              {
                path: '/docs/cli/info',
                component: ComponentCreator('/docs/cli/info', '1b9'),
                exact: true,
                sidebar: "cliSidebar"
              },
              {
                path: '/docs/cli/list',
                component: ComponentCreator('/docs/cli/list', '463'),
                exact: true,
                sidebar: "cliSidebar"
              },
              {
                path: '/docs/cli/migrate',
                component: ComponentCreator('/docs/cli/migrate', 'f00'),
                exact: true,
                sidebar: "cliSidebar"
              },
              {
                path: '/docs/cli/module',
                component: ComponentCreator('/docs/cli/module', 'a98'),
                exact: true,
                sidebar: "cliSidebar"
              },
              {
                path: '/docs/cli/templates',
                component: ComponentCreator('/docs/cli/templates', '071'),
                exact: true,
                sidebar: "cliSidebar"
              },
              {
                path: '/docs/configuration/appsettings',
                component: ComponentCreator('/docs/configuration/appsettings', '61f'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/configuration/build-system',
                component: ComponentCreator('/docs/configuration/build-system', 'ec5'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/configuration/central-package-management',
                component: ComponentCreator('/docs/configuration/central-package-management', '0ec'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/data/entity-framework',
                component: ComponentCreator('/docs/data/entity-framework', '72a'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/data/migrations',
                component: ComponentCreator('/docs/data/migrations', '941'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/data/mongodb',
                component: ComponentCreator('/docs/data/mongodb', '4d6'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/data/overview',
                component: ComponentCreator('/docs/data/overview', '771'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/data/repositories',
                component: ComponentCreator('/docs/data/repositories', '19f'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/deployment',
                component: ComponentCreator('/docs/deployment', '9a4'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/getting-started/first-module',
                component: ComponentCreator('/docs/getting-started/first-module', '7ac'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/getting-started/prerequisites',
                component: ComponentCreator('/docs/getting-started/prerequisites', '5fc'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/getting-started/quick-start',
                component: ComponentCreator('/docs/getting-started/quick-start', '835'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/hardening/correlation',
                component: ComponentCreator('/docs/hardening/correlation', '55f'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/hardening/cors',
                component: ComponentCreator('/docs/hardening/cors', '2f1'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/hardening/feature-flags',
                component: ComponentCreator('/docs/hardening/feature-flags', 'a97'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/hardening/health-checks',
                component: ComponentCreator('/docs/hardening/health-checks', '885'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/hardening/idempotency',
                component: ComponentCreator('/docs/hardening/idempotency', 'c79'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/hardening/personal-data-protection',
                component: ComponentCreator('/docs/hardening/personal-data-protection', '2ca'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/hardening/rate-limiting',
                component: ComponentCreator('/docs/hardening/rate-limiting', 'e6d'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/hardening/secrets-guard',
                component: ComponentCreator('/docs/hardening/secrets-guard', '6cb'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/hardening/security-headers',
                component: ComponentCreator('/docs/hardening/security-headers', 'ea4'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/identity/external-providers',
                component: ComponentCreator('/docs/identity/external-providers', '335'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/identity/openiddict',
                component: ComponentCreator('/docs/identity/openiddict', '3b1'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/identity/overview',
                component: ComponentCreator('/docs/identity/overview', 'b22'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/introduction',
                component: ComponentCreator('/docs/introduction', '894'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/messaging/events',
                component: ComponentCreator('/docs/messaging/events', '2e1'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/messaging/inbox',
                component: ComponentCreator('/docs/messaging/inbox', '84f'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/messaging/kafka',
                component: ComponentCreator('/docs/messaging/kafka', 'e9f'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/messaging/mediator',
                component: ComponentCreator('/docs/messaging/mediator', 'c73'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/messaging/outbox',
                component: ComponentCreator('/docs/messaging/outbox', '7ed'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/messaging/overview',
                component: ComponentCreator('/docs/messaging/overview', '6eb'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/messaging/rabbitmq',
                component: ComponentCreator('/docs/messaging/rabbitmq', 'ccd'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/observability/opentelemetry',
                component: ComponentCreator('/docs/observability/opentelemetry', 'ff9'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/observability/overview',
                component: ComponentCreator('/docs/observability/overview', '6ca'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/platform/authorization',
                component: ComponentCreator('/docs/platform/authorization', '80f'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/platform/background-jobs',
                component: ComponentCreator('/docs/platform/background-jobs', '8a3'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/platform/caching',
                component: ComponentCreator('/docs/platform/caching', 'a44'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/platform/multi-tenancy',
                component: ComponentCreator('/docs/platform/multi-tenancy', '09e'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/platform/overview',
                component: ComponentCreator('/docs/platform/overview', '4b0'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/platform/signalr',
                component: ComponentCreator('/docs/platform/signalr', '171'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/platform/storage',
                component: ComponentCreator('/docs/platform/storage', '83e'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/samples',
                component: ComponentCreator('/docs/samples', 'e88'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/testing/integration-tests',
                component: ComponentCreator('/docs/testing/integration-tests', 'f3d'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/docs/testing/overview',
                component: ComponentCreator('/docs/testing/overview', '66f'),
                exact: true,
                sidebar: "docsSidebar"
              }
            ]
          }
        ]
      }
    ]
  },
  {
    path: '/',
    component: ComponentCreator('/', 'e5f'),
    exact: true
  },
  {
    path: '*',
    component: ComponentCreator('*'),
  },
];
