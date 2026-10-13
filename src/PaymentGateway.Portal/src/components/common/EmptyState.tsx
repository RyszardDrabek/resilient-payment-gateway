import React from 'react';
import { CheckCircle2 } from 'lucide-react';

interface EmptyStateProps {
  title?: string;
  description?: string;
}

export const EmptyState: React.FC<EmptyStateProps> = ({
  title = 'No Unresolved Payments Found',
  description = 'All payments are currently reconciled, or no payments match the active filter criteria.',
}) => {
  return (
    <div className="flex flex-col items-center justify-center py-16 px-4 text-center bg-[#111827] border border-[#374151] rounded-lg">
      <div className="w-12 h-12 rounded-full bg-emerald-500/15 border border-emerald-500/30 flex items-center justify-center mb-4 text-emerald-400">
        <CheckCircle2 className="w-6 h-6" />
      </div>
      <h3 className="text-base font-semibold text-white mb-1">{title}</h3>
      <p className="text-sm text-gray-400 max-w-sm">{description}</p>
    </div>
  );
};
