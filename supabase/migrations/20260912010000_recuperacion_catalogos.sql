-- Complemento al incremento YA existente. No recrea tablas ni borra registros.
-- Aplicar primero en desarrollo; funciones nuevas/reemplazables con misma firma.
begin;
create or replace function public.recuperar_orden_servicios(p_usuario uuid,p_client_order uuid)
returns jsonb language plpgsql security definer set search_path=public,pg_temp as $$
declare o public.ordenes_escolta; v_role text; fields jsonb; items jsonb;
begin
 select role into v_role from public.users where id=p_usuario and active;
 if v_role is null or v_role not in ('admin','operator','administrativo','escolta') then
   raise exception using errcode='42501',message='Acceso denegado';
 end if;
 -- Incluso un administrador solo recupera aquí su propio borrador editable.
 select * into o from public.ordenes_escolta where created_by=p_usuario and client_order_id=p_client_order;
 if not found then return null; end if;
 select coalesce(jsonb_agg(jsonb_build_object('clientItemId',client_item_id,'maquina',coalesce(maquina,''),
   'origen',coalesce(origen,''),'destino',coalesce(destino,'')) order by posicion),'[]'::jsonb),
   coalesce(jsonb_agg(jsonb_build_object('clientItemId',client_item_id,'itemId',id,'servicioId',servicio_id)
   order by posicion),'[]'::jsonb) into fields,items
 from public.ordenes_escolta_items where orden_id=o.id;
 return jsonb_build_object('clientOrderId',o.client_order_id,'version',o.version,
   'fields',jsonb_build_object('fecha',o.fecha,'empresa',coalesce(o.empresa,''),'placaCamabaja',coalesce(o.placa_camabaja,''),
   'placaEscolta',coalesce(o.placa_escolta,''),'nombreEscolta',coalesce(o.nombre_escolta,''),
   'observaciones',coalesce(o.observaciones,''),'clienteId',o.cliente_id,
   'clienteDocumentoSnapshot',o.cliente_documento_snapshot,'vehiculoPlacaSnapshot',o.vehiculo_placa_snapshot,'viajes',fields),
   'result',jsonb_build_object('id',o.id,'version',o.version,'consecutivo',o.consecutivo,'estado',o.estado_captura,'items',items));
end $$;
revoke all on function public.recuperar_orden_servicios(uuid,uuid) from public,anon,authenticated;
grant execute on function public.recuperar_orden_servicios(uuid,uuid) to service_role;

create or replace function public.clientes_para_orden(p_usuario uuid)
returns jsonb language plpgsql security definer set search_path=public,pg_temp as $$
declare v_role text; v_result jsonb;
begin
 select role into v_role from public.users where id=p_usuario and active;
 if v_role is null or v_role not in ('admin','operator','administrativo','escolta') then
   raise exception using errcode='42501',message='Acceso denegado';
 end if;
 select coalesce(jsonb_agg(x order by x.nombre),'[]'::jsonb) into v_result from (
   select c.id,c.tipo_cliente,c.nombre,c.razon_social,c.nit_o_documento,c.activo,
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
