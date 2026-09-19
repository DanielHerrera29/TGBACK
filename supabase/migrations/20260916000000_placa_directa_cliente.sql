-- Placa de carga capturada al crear cliente. No requiere ficha RNDC.
-- Los vínculos históricos de cliente_vehiculos se conservan.
begin;
alter table public.clientes add column if not exists placa_carga text;
do $$ begin
 if not exists(select 1 from pg_constraint where conrelid='public.clientes'::regclass and conname='clientes_placa_carga_formato') then
  alter table public.clientes add constraint clientes_placa_carga_formato
   check (placa_carga is null or placa_carga ~ '^[A-Z]{3}[0-9]{3}$');
 end if;
end $$;

create or replace function public.clientes_para_orden(p_usuario uuid)
returns jsonb language plpgsql security definer set search_path=public,pg_temp as $$
declare v_role text; v_result jsonb;
begin
 select role into v_role from public.users where id=p_usuario and active;
 if v_role is null or v_role not in ('admin','operator','administrativo','escolta') then
   raise exception using errcode='42501',message='Acceso denegado';
 end if;
 select coalesce(jsonb_agg(x order by x.nombre),'[]'::jsonb) into v_result from (
   select c.id,c.tipo_cliente,c.nombre,c.razon_social,c.nit_o_documento,c.activo,c.placa_carga,
     coalesce((select jsonb_agg(jsonb_build_object('activo',true,'vehiculos',
       jsonb_build_object('id',v.id,'num_placa',v.num_placa,'estado',v.estado)) order by v.num_placa)
       from public.cliente_vehiculos cv join public.vehiculos v on v.id=cv.vehiculo_id
       where cv.cliente_id=c.id and cv.activo and v.estado is distinct from 'inactive'),'[]'::jsonb) as cliente_vehiculos
   from public.clientes c where c.activo
 ) x;
 return v_result;
end $$;
revoke all on function public.clientes_para_orden(uuid) from public,anon,authenticated;
grant execute on function public.clientes_para_orden(uuid) to service_role;
commit;
-- Reversión de aplicación: volver al backend/frontend anteriores conserva
-- la columna y los datos. No eliminar placa_carga ni sus valores.
