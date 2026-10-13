import { useState } from 'react';
import { AuthProvider } from './context/AuthContext';
import { AppShell } from './components/layout/AppShell';
import { UnresolvedConsole } from './components/unresolved/UnresolvedConsole';
import { Radio, ShieldAlert } from 'lucide-react';

function PortalContent() {
  const [activeTab, setActiveTab] = useState<'unresolved' | 'webhooks' | 'risk'>('unresolved');

  return (
    <AppShell activeTab={activeTab} onTabChange={setActiveTab}>
      {activeTab === 'unresolved' && <UnresolvedConsole />}

      {activeTab === 'webhooks' && (
        <div className="bg-[#111827] border border-[#374151] rounded-lg p-8 text-center">
          <div className="w-12 h-12 rounded-full bg-purple-500/20 text-purple-400 mx-auto flex items-center justify-center mb-4">
            <Radio className="w-6 h-6" />
          </div>
          <h2 className="text-xl font-bold text-white mb-2">Merchant Webhook Management & Simulator</h2>
          <p className="text-sm text-gray-400 max-w-md mx-auto mb-4">
            Self-service subscription registration, HMAC secret rotation, and payload dispatcher simulator.
          </p>
          <span className="inline-block px-3 py-1 bg-purple-500/10 border border-purple-500/30 text-purple-300 rounded text-xs font-mono">
            Scheduled in F-PORTAL-02
          </span>
        </div>
      )}

      {activeTab === 'risk' && (
        <div className="bg-[#111827] border border-[#374151] rounded-lg p-8 text-center">
          <div className="w-12 h-12 rounded-full bg-rose-500/20 text-rose-400 mx-auto flex items-center justify-center mb-4">
            <ShieldAlert className="w-6 h-6" />
          </div>
          <h2 className="text-xl font-bold text-white mb-2">Compliance Risk Flags & Disposition Console</h2>
          <p className="text-sm text-gray-400 max-w-md mx-auto mb-4">
            AML & anomaly score review queue with manual auditor disposition workflows.
          </p>
          <span className="inline-block px-3 py-1 bg-rose-500/10 border border-rose-500/30 text-rose-300 rounded text-xs font-mono">
            Scheduled in F-PORTAL-03
          </span>
        </div>
      )}
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
