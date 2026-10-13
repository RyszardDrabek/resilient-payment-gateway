import React from 'react';

interface BadgeProps {
  variant?: 'success' | 'warning' | 'destructive' | 'accent' | 'muted';
  children: React.ReactNode;
}

export const Badge: React.FC<BadgeProps> = ({ variant = 'muted', children }) => {
  const variantStyles = {
    success: 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30',
    warning: 'bg-amber-500/15 text-amber-400 border-amber-500/30',
    destructive: 'bg-rose-500/15 text-rose-400 border-rose-500/30',
    accent: 'bg-purple-500/15 text-purple-400 border-purple-500/30',
    muted: 'bg-gray-500/15 text-gray-300 border-gray-500/30',
  }[variant];

  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-semibold border ${variantStyles}`}>
      {children}
    </span>
  );
};

export const StateBadge: React.FC<{ state: string }> = ({ state }) => {
  const normalized = state.toLowerCase();
  if (normalized === 'settled' || normalized === 'authorized') {
    return <Badge variant="success">{state}</Badge>;
  }
  if (normalized === 'pending') {
    return <Badge variant="warning">{state}</Badge>;
  }
  if (normalized === 'unknown') {
    return <Badge variant="muted">{state}</Badge>;
  }
  if (normalized === 'failed' || normalized === 'declined' || normalized === 'cancelled') {
    return <Badge variant="destructive">{state}</Badge>;
  }
  return <Badge variant="accent">{state}</Badge>;
};

export const ChannelBadge: React.FC<{ channel: string }> = ({ channel }) => {
  const isWeb3 = channel.toLowerCase().includes('web3');
  return (
    <Badge variant={isWeb3 ? 'accent' : 'muted'}>
      {channel}
    </Badge>
  );
};
