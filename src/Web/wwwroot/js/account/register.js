document.addEventListener('DOMContentLoaded', () => {
  const role = document.getElementById('registration-role');
  const code = document.getElementById('role-code-wrap');
  if (!role || !code) return;

  const updateRoleCodeVisibility = () => {
    code.hidden = role.value === 'User';
  };
  role.addEventListener('change', updateRoleCodeVisibility);
  updateRoleCodeVisibility();
});
