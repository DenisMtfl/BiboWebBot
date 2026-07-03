window.biboCredentialsStorage = {
  saveMany: function (key, items) {
    const payload = JSON.stringify(Array.isArray(items) ? items : []);
    localStorage.setItem(key, payload);
  },

  loadMany: function (key) {
    const raw = localStorage.getItem(key);
    if (!raw) {
      return [];
    }

    try {
      const parsed = JSON.parse(raw);
      if (Array.isArray(parsed)) {
        return parsed;
      }

      if (parsed && typeof parsed === "object") {
        return [parsed];
      }

      return [];
    } catch {
      return [];
    }
  },

  clear: function (key) {
    localStorage.removeItem(key);
  },

  saveSelection: function (key, selectedValues) {
    const payload = JSON.stringify(Array.isArray(selectedValues) ? selectedValues : []);
    localStorage.setItem(key, payload);
  },

  loadSelection: function (key) {
    const raw = localStorage.getItem(key);
    if (!raw) {
      return [];
    }

    try {
      const parsed = JSON.parse(raw);
      return Array.isArray(parsed) ? parsed : [];
    } catch {
      return [];
    }
  },

  syncEarliestLoanToGoogleCalendar: async function (dueDate, accountLabel, calendarId, eventName) {
    if (!dueDate) {
      return false;
    }

    try {
      const params = new URLSearchParams({ dueDate });
      if (accountLabel) {
        params.set("accountLabel", accountLabel);
      }
      if (calendarId) {
        params.set("calendarId", calendarId);
      }
      if (eventName) {
        params.set("eventName", eventName);
      }

      const response = await fetch(`/api/google-calendar/sync-earliest?${params.toString()}`, {
        method: "GET",
        credentials: "include"
      });

      return response.ok;
    } catch {
      return false;
    }
  },

  getGoogleCalendars: async function () {
    try {
      const response = await fetch("/api/google-calendar/calendars", {
        method: "GET",
        credentials: "include"
      });

      if (!response.ok) {
        return { isSuccess: false, errorMessage: `Kalenderliste konnte nicht geladen werden (HTTP ${response.status}).`, calendars: [] };
      }

      const parsed = await response.json();
      return {
        isSuccess: parsed?.isSuccess ?? false,
        errorMessage: parsed?.errorMessage ?? null,
        calendars: Array.isArray(parsed?.calendars) ? parsed.calendars : []
      };
    } catch {
      return { isSuccess: false, errorMessage: "Kalenderliste konnte nicht geladen werden (Netzwerkfehler).", calendars: [] };
    }
  },

  saveGoogleCalendarSettings: function (key, settings) {
    localStorage.setItem(key, JSON.stringify(settings ?? {}));
  },

  loadGoogleCalendarSettings: function (key) {
    const raw = localStorage.getItem(key);
    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw);
    } catch {
      return null;
    }
  }
};
