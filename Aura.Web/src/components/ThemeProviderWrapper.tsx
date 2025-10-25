import { ReactNode } from 'react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';

interface ThemeProviderWrapperProps {
  children: ReactNode;
  isDarkMode: boolean;
}

export function ThemeProviderWrapper({ children, isDarkMode }: ThemeProviderWrapperProps) {
  return (
    <FluentProvider theme={isDarkMode ? webDarkTheme : webLightTheme}>
      {children}
    </FluentProvider>
  );
}
