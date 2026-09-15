-- Complemento: enlaza vehículos de carga YA registrados. No crea vehículos
-- incompletos ni convierte un vehículo de escolta en vehículo RNDC.
begin;
create or replace function public.vehiculos_para_vincular_cliente(p_usuario uuid)
returns jsonb language plpgsql security definer set search_path=public,pg_temp as $$
begin
 if not exists(select 1 from public.users where id=p_usuario and active and role in ('admin','administrativo')) then
  raise exception using errcode='42501',message='Acceso denegado';
 end if;
 return (select coalesce(jsonb_agg(jsonb_build_object('id',id,'placa',num_placa) order by num_placa),'[]'::jsonb)
 from public.vehiculos where estado is distinct from 'inactive');
end $$;
create or replace function public.vincular_vehiculo_cliente(p_usuario uuid,p_cliente uuid,p_vehiculo uuid)
returns void language plpgsql security definer set search_path=public,pg_temp as $$
begin
 if not exists(select 1 from public.users where id=p_usuario and active and role in ('admin','administrativo')) then
  raise exception using errcode='42501',message='Acceso denegado';
 end if;
 perform 1 from public.clientes where id=p_cliente and activo for update;
 if not found then raise exception using errcode='22023',message='Cliente no disponible'; end if;
 perform 1 from public.vehiculos where id=p_vehiculo and estado is distinct from 'inactive' for share;
 if not found then raise exception using errcode='22023',message='Vehículo no disponible'; end if;
 insert into public.cliente_vehiculos(cliente_id,vehiculo_id,activo,es_principal)
 values(p_cliente,p_vehiculo,true,false)
 on conflict(cliente_id,vehiculo_id) do update set activo=true;
end $$;
revoke all on function public.vehiculos_para_vincular_cliente(uuid),public.vincular_vehiculo_cliente(uuid,uuid,uuid) from public,anon,authenticated;
grant execute on function public.vehiculos_para_vincular_cliente(uuid),public.vincular_vehiculo_cliente(uuid,uuid,uuid) to service_role;
commit;
