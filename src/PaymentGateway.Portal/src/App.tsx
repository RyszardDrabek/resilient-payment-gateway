import { useState } from 'react';
import { AuthProvider } from './context/AuthContext';
import { AppShell } from './components/layout/AppShell';
import { UnresolvedConsole } from './components/unresolved/UnresolvedConsole';
import { WebhooksConsole } from './components/webhooks/WebhooksConsole';
import { RiskConsole } from './components/risk/RiskConsole';

function PortalContent() {
  const [activeTab, setActiveTab] = useState<'unresolved' | 'webhooks' | 'risk'>('unresolved');

  return (
    <AppShell activeTab={activeTab} onTabChange={setActiveTab}>
      {activeTab === 'unresolved' && <UnresolvedConsole />}

      {activeTab === 'webhooks' && <WebhooksConsole />}

      {activeTab === 'risk' && <RiskConsole />}
    </AppShell>
  );
}

export function App() {
  return (
    <AuthProvider>
      <PortalContent />
    </AuthProvider>
  );
}

export default App;
