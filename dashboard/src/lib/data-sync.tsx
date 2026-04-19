"use client";

import { createContext, useContext, useEffect, useState, useCallback } from "react";
import { api } from "@/lib/api";
import { getStoredBusiness, getStoredUser } from "@/lib/auth";
import type { BusinessDto, UserDto } from "@/lib/types";

interface DataSyncState {
  business: BusinessDto | null;
  user: UserDto | null;
  refresh: () => void;
}

const DataSyncContext = createContext<DataSyncState>({
  business: null,
  user: null,
  refresh: () => {},
});

export function DataSyncProvider({ children }: { children: React.ReactNode }) {
  const [business, setBusiness] = useState<BusinessDto | null>(() => getStoredBusiness());
  const [user, setUser] = useState<UserDto | null>(() => getStoredUser());

  const sync = useCallback(() => {
    Promise.all([
      api.get<{ data: BusinessDto }>("/business"),
      api.get<{ data: UserDto }>("/auth/me"),
    ]).then(([bizRes, userRes]) => {
      if (bizRes.data.data) {
        setBusiness(bizRes.data.data);
        localStorage.setItem("bp_business", JSON.stringify(bizRes.data.data));
      }
      if (userRes.data.data) {
        setUser(userRes.data.data);
        localStorage.setItem("bp_user", JSON.stringify(userRes.data.data));
      }
    }).catch(() => {});
  }, []);

  useEffect(() => { sync(); }, [sync]);

  return (
    <DataSyncContext.Provider value={{ business, user, refresh: sync }}>
      {children}
    </DataSyncContext.Provider>
  );
}

export function useBusiness() {
  return useContext(DataSyncContext).business;
}

export function useUser() {
  return useContext(DataSyncContext).user;
}

export function useDataSync() {
  return useContext(DataSyncContext);
}

export function useUpdateBusiness() {
  const ctx = useContext(DataSyncContext);
  return (updated: BusinessDto) => {
    (ctx as unknown as { business: BusinessDto | null }).business = updated;
    localStorage.setItem("bp_business", JSON.stringify(updated));
    ctx.refresh();
  };
}
