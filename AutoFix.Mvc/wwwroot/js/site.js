// Confirmación para formularios con data-confirmar="mensaje"
document.addEventListener('submit', function (e) {
  const msg = e.target.getAttribute('data-confirmar');
  if (msg && !confirm(msg)) e.preventDefault();
});

// Validación rápida en el navegador de imágenes (la validación real la hace la API)
document.addEventListener('change', function (e) {
  const input = e.target;
  if (input.type !== 'file' || !input.files || !input.files[0]) return;
  const f = input.files[0];
  const ok = /\.(jpe?g|png)$/i.test(f.name);
  const aviso = input.parentElement.querySelector('.aviso-archivo');
  let texto = '';
  if (!ok) texto = 'Solo se permiten JPG, JPEG o PNG (la API lo rechazará).';
  else if (f.size > 5 * 1024 * 1024) texto = 'La imagen supera 5 MB (la API la rechazará).';
  if (aviso) aviso.textContent = texto;
});
