import React from 'react';

export const CardSkeleton: React.FC = () => (
  <div className="bg-[#111827] border border-[#374151] rounded-lg p-4 animate-pulse">
    <div className="h-3 bg-[#1f2937] rounded w-1/3 mb-3"></div>
    <div className="h-8 bg-[#1f2937] rounded w-1/2 mb-2"></div>
    <div className="h-3 bg-[#1f2937] rounded w-2/3"></div>
  </div>
);

export const TableSkeleton: React.FC<{ rows?: number }> = ({ rows = 5 }) => (
  <div className="w-full bg-[#111827] border border-[#374151] rounded-lg overflow-hidden animate-pulse">
    <div className="h-10 bg-[#1f2937] border-b border-[#374151]"></div>
    <div className="divide-y divide-[#374151]">
      {Array.from({ length: rows }).map((_, i) => (
        <div key={i} className="h-14 bg-[#111827] px-4 flex items-center gap-4">
          <div className="h-4 bg-[#1f2937] rounded w-28"></div>
          <div className="h-4 bg-[#1f2937] rounded w-16"></div>
          <div className="h-4 bg-[#1f2937] rounded w-20"></div>
          <div className="h-4 bg-[#1f2937] rounded w-32"></div>
          <div className="h-4 bg-[#1f2937] rounded w-24"></div>
          <div className="h-6 bg-[#1f2937] rounded w-16 ml-auto"></div>
        </div>
      ))}
    </div>
  </div>
);
