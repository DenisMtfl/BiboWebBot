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

  syncEarliestLoanToGoogleCalendar: async function (dueDate, accountLabel) {
    if (!dueDate) {
      return false;
    }

    try {
      const params = new URLSearchParams({ dueDate });
      if (accountLabel) {
        params.set("accountLabel", accountLabel);
      }

      const response = await fetch(`/api/google-calendar/sync-earliest?${params.toString()}`, {
        method: "GET",
        credentials: "include"
      });

      return response.ok;
    } catch {
      return false;
    }
  }
};
