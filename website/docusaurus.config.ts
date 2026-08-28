import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const config: Config = {
  title: 'Modulus Framework',
  tagline: 'A modular-monolith framework for .NET 10',
  favicon: 'img/favicon.ico',

  url: 'https://modulus.dev',
  baseUrl: '/',

  organizationName: 'cobytelabs',
  projectName: 'modulus',

  onBrokenLinks: 'throw',

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
          editUrl: 'https://github.com/cobytelabs/modulus/tree/main/website/',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    image: 'img/modulus-social-card.png',
    colorMode: {
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'Modulus',
      logo: {
        alt: 'Modulus Logo',
        src: 'img/logo.svg',
      },
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'docsSidebar',
          position: 'left',
          label: 'Docs',
        },
        {
          type: 'docSidebar',
          sidebarId: 'cliSidebar',
          position: 'left',
          label: 'CLI',
        },
        {
          type: 'docSidebar',
          sidebarId: 'apiSidebar',
          position: 'left',
          label: 'API',
        },
        {
          href: 'https://github.com/cobytelabs/modulus',
          label: 'GitHub',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Documentation',
          items: [
            { label: 'Getting Started', to: '/docs/introduction' },
            { label: 'Architecture', to: '/docs/architecture/overview' },
            { label: 'CLI Reference', to: '/docs/cli/' },
          ],
        },
        {
          title: 'Community',
          items: [
            { label: 'GitHub', href: 'https://github.com/cobytelabs/modulus' },
            { label: 'Issues', href: 'https://github.com/cobytelabs/modulus/issues' },
          ],
        },
        {
          title: 'More',
          items: [
            { label: 'NuGet', href: 'https://www.nuget.org/profiles/Cobytelabs' },
            { label: 'Samples', href: 'https://github.com/cobytelabs/modulus/tree/main/samples' },
          ],
        },
      ],
      copyright: `Copyright ${new Date().getFullYear()} Cobytelabs. Built with Docusaurus.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['csharp', 'bash', 'json', 'toml', 'yaml'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
