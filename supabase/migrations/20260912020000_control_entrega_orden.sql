-- Nuevo registro de entrega. No recrea órdenes ni servicios existentes.
-- Un envío con respuesta incierta requiere conciliación, nunca reenvío ciego.
begin;
create table public.orden_entrega_control (
 orden_id uuid primary key references public.ordenes_escolta(id) on delete restrict,
 pdf_hash text not null,
 estado text not null check (estado in ('ENVIANDO','ENVIADA','ERROR_PREVIO','POR_VERIFICAR')),
 updated_at timestamptz not null default now()
);
alter table public.orden_entrega_control enable row level security;
revoke all on public.orden_entrega_control from anon,authenticated;
grant all on public.orden_entrega_control to service_role;

create function public.reclamar_entrega_orden(p_usuario uuid,p_orden uuid,p_hash text)
returns text language plpgsql security definer set search_path=public,pg_temp as $$
declare o public.ordenes_escolta; e public.orden_entrega_control;
begin
 if not exists(select 1 from public.users where id=p_usuario and active and role in ('admin','operator','administrativo','escolta')) then
   raise exception using errcode='42501',message='Acceso denegado';
 end if;
 select * into o from public.ordenes_escolta where id=p_orden and created_by=p_usuario for update;
 if not found or o.estado_captura <> 'CONFIRMADA' then
   raise exception using errcode='42501',message='Orden no confirmada o ajena';
 end if;
 if o.email_enviado_at is not null then return 'ENVIADA'; end if;
 select * into e from public.orden_entrega_control where orden_id=p_orden;
 if found then
   if e.estado='ENVIADA' then return 'ENVIADA'; end if;
   if e.estado in ('ENVIANDO','POR_VERIFICAR') then return 'POR_VERIFICAR'; end if;
   -- ERROR_PREVIO: no se inició el correo; es seguro reconstruir el PDF.
   update public.orden_entrega_control set estado='ENVIANDO',pdf_hash=p_hash,updated_at=now() where orden_id=p_orden;
 else
   insert into public.orden_entrega_control(orden_id,pdf_hash,estado) values(p_orden,p_hash,'ENVIANDO');
 end if;
 return 'RECLAMADA';
end $$;
create function public.finalizar_entrega_orden(p_usuario uuid,p_orden uuid,p_estado text)
returns void language plpgsql security definer set search_path=public,pg_temp as $$
begin
 if p_estado not in ('ENVIADA','ERROR_PREVIO','POR_VERIFICAR') then
   raise exception using errcode='22023',message='Estado no válido';
 end if;
 if not exists(select 1 from public.ordenes_escolta where id=p_orden and created_by=p_usuario) then
   raise exception using errcode='42501',message='Orden ajena';
 end if;
 update public.orden_entrega_control set estado=p_estado,updated_at=now()
 where orden_id=p_orden and estado='ENVIANDO';
 if p_estado='ENVIADA' then
   update public.ordenes_escolta set email_enviado_at=coalesce(email_enviado_at,now()),email_error=null where id=p_orden
   and exists(select 1 from public.orden_entrega_control where orden_id=p_orden and estado='ENVIADA');
 end if;
end $$;
revoke all on function public.reclamar_entrega_orden(uuid,uuid,text),public.finalizar_entrega_orden(uuid,uuid,text) from public,anon,authenticated;
grant execute on function public.reclamar_entrega_orden(uuid,uuid,text),public.finalizar_entrega_orden(uuid,uuid,text) to service_role;
commit;
